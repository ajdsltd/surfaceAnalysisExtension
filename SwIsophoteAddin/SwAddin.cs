// Surface Analysis Extension
// Andrew Jackson - AJ Design Studio LTD (https://ajdesignstudio.co.nz)
// Version 1.0.2


using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SolidWorks.Interop.swpublished;

namespace SwIsophoteAddin
{
    // ---------------------------------------------------------------
    // GUID: generate your own (Tools > Create GUID in Visual Studio,
    // or Guid.NewGuid() once and hardcode it). Must stay constant --
    // this is how SOLIDWORKS + the registry identify the add-in.
    // ---------------------------------------------------------------
    // GUID changed from the original 6F1A2B3C-... to a fresh one:
    // this exact add-in has now crashed SW during ConnectToSW/Activate()
    // roughly a dozen times while debugging the CommandManager toggle
    // button. Every fix attempted so far reset state scoped to the
    // *command group* (ID bumps, ignorePrevious) -- this is the first
    // attempt at resetting state scoped to the *add-in itself*, in case
    // SW keeps any per-add-in cached/registered state keyed by GUID that
    // repeated crashes during connection could have left corrupted.
    // NOTE: the old GUID's registry entries become orphaned, harmless
    // leftovers -- no cleanup needed, but if this doesn't help, it's
    // worth reverting so the add-in's identity doesn't churn further.
    [Guid("0256F20B-F67B-42AA-AA1A-8D7A66277096")]
    [ComVisible(true)]
    // AutoDispatch, not None: SW resolves AddCommandItem2's callback
    // strings ("OnToggleAddinClick"/"OnToggleAddinEnable") by looking
    // them up on this COM object at Activate() time. With
    // ClassInterfaceType.None, COM clients can ONLY see members of the
    // explicitly implemented ISwAddin interface (ConnectToSW/
    // DisconnectFromSW) -- OnToggleAddinClick/OnToggleAddinEnable are
    // not part of ISwAddin, so they were completely invisible to COM,
    // and SW's callback lookup had nothing to find. This is what was
    // actually causing the AccessViolationException on Activate() --
    // confirmed by a bare-minimum CreateCommandGroup2/AddCommandItem2/
    // Activate() test crashing identically regardless of icons, group
    // ID, or ignorePrevious, which ruled out everything else first.
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class SwAddin : ISwAddin
    {
        private SldWorks iSwApp;
        private int addinCookie;

        private ModelDoc2 activeDoc;
        private PartDoc activePart;
        private ModelView activeView;

        // See OnBufferSwapNotify -- once set, all further GL drawing is
        // skipped for this document's session (reset on doc switch, see
        // AttachToActiveDoc). Only reached after recovery attempts are
        // exhausted; a genuine self-healing attempt is tried first.
        private bool glContextAppearsBroken = false;
        private int glContextRecoveryAttempts = 0;
        private const int MAX_GL_CONTEXT_RECOVERY_ATTEMPTS = 2;

        // GPU buffers for the active part. MeshBuffer stores position and
        // normal data; LineBuffer stores position-only line segments.
        // Rendering uses OpenGL's compatibility-profile client-state arrays
        // so it can coexist with SOLIDWORKS' legacy OpenGL pipeline.
        private MeshBuffer meshBuffer;
        private LineBuffer lineBuffer;
        private LineBuffer isocurveBuffer;
        private ShaderProgram shaderProgram;
        private MeshSettingsPanel settingsPanel;

        // CommandManager toggle button: single combined switch that
        // shows/hides settingsPanel and enables/disables the overlay
        // together, rather than two separate controls. Starts OFF --
        // unlike the old unconditional Show() in ConnectToSW, the panel
        // now stays hidden until this button is clicked.
        private ICommandManager iCmdMgr;
        private ICommandGroup cmdGroup;
        // mainCmdGroupID bumped 5 -> 6 -> 7: every previous ID has now
        // crashed on Activate() at least once, and a crashed Activate()
        // can leave partially-written/corrupted registry entries behind
        // for that ID that make even a correct subsequent attempt crash
        // again at the same spot. Moving to a genuinely never-crashed ID
        // rules that out cleanly.
        private const int mainCmdGroupID = 7;
        private const int cmdToggleID = 0;
        private bool AddinActive = false;

        #region ISwAddin implementation

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            try
            {
                iSwApp = (SldWorks)ThisSW;
                addinCookie = Cookie;

                // MISSING UNTIL NOW -- every official SW CommandManager
                // example (Create CommandManager Tab and Tab Boxes,
                // Create Flyouts in the CommandManager, Add Menu and
                // Menu Item) calls this immediately after receiving the
                // app object and cookie, before any CommandManager
                // setup. It registers this object as the one SW should
                // resolve AddCommandItem2's callback-name strings
                // against. Missing it is a far more direct explanation
                // for the Activate() crash than anything tried before.
                iSwApp.SetAddinCallbackInfo2(0, this, addinCookie);

                iCmdMgr = iSwApp.GetCommandManager(addinCookie);

                iSwApp.ActiveDocChangeNotify += OnActiveDocChangeNotify;
                AttachToActiveDoc();

                SetDefaultReferenceDirection();

                // Settings panel for mesh density, display controls,
                // reference direction, zebra/isophote settings,
                // surface-normal inspection, and face isolation.
                settingsPanel = new MeshSettingsPanel(
                    onMeshSettingsChanged: () =>
                    {
                        // Mesh-related settings affect cached geometry,
                        // so discard the body caches when they change.
                        bodyMeshCache.Clear();
                        bodyEdgeCache.Clear();
                        bodyIsocurveCache.Clear();
                        lastKnownBodyState.Clear();
                        InvalidateMeshCache();
                        RequestOverlayRefresh();
                    },
                    onSetPlaneClicked: () =>
                    {
                        SetPlaneFromCurrentView();
                    },
                    onSetAxisXClicked: () =>
                    {
                        SetPlaneToAxis(1f, 0f, 0f);
                    },
                    onSetAxisYClicked: () =>
                    {
                        SetPlaneToAxis(0f, 1f, 0f);
                    },
                    onSetAxisZClicked: () =>
                    {
                        SetPlaneToAxis(0f, 0f, 1f);
                    },
                    onLineSettingsChanged: () =>
                    {
                        RequestOverlayRefresh();
                    },
                    onZebraToggled: () =>
                    {
                        RequestOverlayRefresh();
                    },
                    onNormalsToggleClicked: () =>
                    {
                        ToggleNormals();
                    },
                    onIsolateSelectedClicked: () =>
                    {
                        IsolateSelectedFaces();
                    },
                    onClearIsolationClicked: () =>
                    {
                        ClearIsolation();
                    },
                    onTestEdgeContinuityClicked: () =>
                    {
                        TestEdgeContinuity();
                    },
                    onCombSettingsChangedClicked: () =>
                    {
                        // Comb geometry (native SW wire bodies) depends on
                        // CombScale/CombToleranceAbsPerMM directly -- rebuild
                        // it immediately rather than going through the GL
                        // render-hook dirty-flag path used by mesh/isocurves.
                        RebuildComb();
                    },
                    onClosedByUserClicked: () =>
                    {
                        // Closing via the window's own X button behaves
                        // exactly like clicking the toggle button off:
                        // every add-in graphic overlay is cleared.
                        AddinActive = false;
                        ClearAllAddinGraphics();
                    },
                    onShowIsocurvesForSelectedClicked: () =>
                    {
                        OnShowIsocurvesForSelectedFacesClicked();
                    });
                // Panel starts hidden -- visibility (and overlay on/off
                // together) is now driven entirely by the CommandManager
                // toggle button below, not shown automatically here.

                SetupCommandManagerToggle();

                Debug.Print("SwIsophoteAddin connected.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.Print("ConnectToSW failed: " + ex);
                return false;
            }
        }

        public bool DisconnectFromSW()
        {
            DetachFromActiveDoc();

            if (iSwApp != null)
            {
                iSwApp.ActiveDocChangeNotify -= OnActiveDocChangeNotify;
            }

            // ClearNormals() only acts on activeDoc, which DetachFromActiveDoc
            // just nulled -- release every document's own leftover
            // entries directly instead, since at unload all of them
            // need cleaning up, not just whichever was last active.
            foreach (var docId in new List<IntPtr>(normalWireBodiesByDoc.Keys))
            {
                ClearNormalsForDoc(docId);
            }
            ClearG2Marker();
            ClearComb();
            ClearG2Callout();

            iCmdMgr?.RemoveCommandGroup2(mainCmdGroupID, true);
            iCmdMgr = null;
            cmdGroup = null;

            settingsPanel?.Close();
            settingsPanel?.Dispose();
            settingsPanel = null;

            meshBuffer?.Dispose();
            lineBuffer?.Dispose();
            isocurveBuffer?.Dispose();
            shaderProgram?.Dispose();
            meshBuffer = null;
            lineBuffer = null;
            isocurveBuffer = null;
            shaderProgram = null;

            deferredRefreshTimer?.Stop();
            deferredRefreshTimer?.Dispose();
            deferredRefreshTimer = null;

            iSwApp = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();

            return true;
        }

        // Creates a single CommandManager/toolbar/menu toggle button
        // ("Surface Analysis") that shows/hides settingsPanel and
        // enables/disables the overlay together as one combined switch.
        // Called once from ConnectToSW, after iCmdMgr is available.
        private void SetupCommandManagerToggle()
        {
            int cmdGroupErr = 0;

            // Was hardcoded to `true` during the Activate()-crash
            // bisection (a leftover from ruling out corrupted registry
            // state as a hypothesis) and never reverted once the real
            // cause turned out to be the missing SetAddinCallbackInfo2
            // call. SW's own documented guidance for this parameter:
            // "Set IgnorePreviousVersion to true to prevent SolidWorks
            // from saving the current toolbar setting to the registry"
            // -- i.e. hardcoding true was directly causing SW to never
            // persist this toolbar's position (and, per [stated]'s
            // report, apparently destabilizing the rest of the saved
            // layout too) on every single load. Restored to the
            // originally-intended dynamic check: only ignore previous
            // state when the command definitions have genuinely changed
            // since last saved (compared via cmdToggleID), so a normal
            // reload preserves whatever position the user left it in.
            bool ignorePrevious = false;
            object registryIDs;
            if (iCmdMgr.GetGroupDataFromRegistry(mainCmdGroupID, out registryIDs))
            {
                ignorePrevious = !CompareIDs((int[])registryIDs, new int[] { cmdToggleID });
            }

            cmdGroup = iCmdMgr.CreateCommandGroup2(
                mainCmdGroupID, "Surface Analysis", "Surface Analysis add-in",
                "Show/hide the panel and enable/disable the overlay", -1, ignorePrevious, ref cmdGroupErr);

            // We were never actually checking whether this succeeded --
            // if it silently returned null (e.g. some other add-in
            // already owns this exact CommandManager UserID, or the
            // registry state is unusable), every call after this would
            // be operating on a null/broken COM object, which is a very
            // plausible source of a native AccessViolationException
            // downstream at Activate() rather than failing loudly here.
            if (cmdGroup == null)
            {
                Debug.Print("SetupCommandManagerToggle: CreateCommandGroup2 returned null (error code " + cmdGroupErr + "). Aborting setup.");
                return;
            }
            Debug.Print("SetupCommandManagerToggle: CreateCommandGroup2 succeeded (error code " + cmdGroupErr + ").");

            // Icon files must exist and be set BEFORE AddCommandItem2/
            // Activate() -- every official SW example does this, and
            // skipping IconList entirely reproducibly crashed
            // Activate() with an AccessViolationException, so it's not
            // optional. Resolved relative to the built DLL so it works
            // regardless of install path. Files must actually be copied
            // to the output folder alongside SwIsophoteAddin.dll -- see
            // the Icons/ subfolder and each PNG's Build Action (Content)
            // + Copy to Output Directory (Copy if newer) properties.
            // Confirmed via Explorer: VS copies these directly into the
            // output folder alongside SwIsophoteAddin.dll (no "Icons"
            // subfolder is created there, even though the source files
            // live in an Icons/ folder in the project) -- so look right
            // next to the assembly, not in a subfolder.
            string iconDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string[] candidateIcons = new[]
            {
                Path.Combine(iconDir, "draft_toggle_20.png"),
                Path.Combine(iconDir, "draft_toggle_32.png"),
                Path.Combine(iconDir, "draft_toggle_40.png"),
                Path.Combine(iconDir, "draft_toggle_64.png"),
                Path.Combine(iconDir, "draft_toggle_96.png"),
            };
            var foundIcons = new List<string>();
            foreach (var path in candidateIcons)
            {
                bool exists = File.Exists(path);
                Debug.Print((exists ? "Found icon: " : "MISSING icon: ") + path);
                if (exists) foundIcons.Add(path);
            }

            if (foundIcons.Count > 0)
            {
                cmdGroup.IconList = foundIcons.ToArray();
                cmdGroup.MainIconList = foundIcons.ToArray();
            }
            else
            {
                Debug.Print("SetupCommandManagerToggle: no icon files found under " + iconDir + " -- proceeding without IconList/MainIconList.");
            }

            int menuToolbarOption = (int)(swCommandItemType_e.swMenuItem
                                         | swCommandItemType_e.swToolbarItem);

            int addedItemId = cmdGroup.AddCommandItem2(
                "Surface Analysis", -1, "Additional Surface Analysis Tools With User Controllable Mesh Settings", "Surface Analysis",
                0, "OnToggleAddinClick", "OnToggleAddinEnable", cmdToggleID, menuToolbarOption);
            Debug.Print("SetupCommandManagerToggle: AddCommandItem2 returned itemId=" + addedItemId);

            cmdGroup.HasToolbar = true;
            cmdGroup.HasMenu = true;
            Debug.Print("SetupCommandManagerToggle: about to call cmdGroup.Activate()...");
            bool activated = cmdGroup.Activate();
            Debug.Print("SetupCommandManagerToggle: cmdGroup.Activate() returned " + activated);
        }

        // Order-independent comparison of the command IDs SW has on file
        // for this group (from GetGroupDataFromRegistry) against the IDs
        // this version of the add-in actually defines -- used to decide
        // ignorePrevious in SetupCommandManagerToggle above.
        private static bool CompareIDs(int[] storedIDs, int[] addinIDs)
        {
            if (storedIDs == null || storedIDs.Length != addinIDs.Length) return false;
            var stored = new List<int>(storedIDs);
            var addin = new List<int>(addinIDs);
            stored.Sort();
            addin.Sort();
            for (int i = 0; i < addin.Count; i++)
            {
                if (stored[i] != addin[i]) return false;
            }
            return true;
        }

        // Click handler for the CommandManager toggle button. Public,
        // non-static -- SW resolves EnableMethod/CallbackFunction names
        // against this add-in's own COM instance (same reason
        // ISwAddin's ConnectToSW/DisconnectFromSW are public instance
        // methods).
        public void OnToggleAddinClick()
        {
            AddinActive = !AddinActive;

            if (AddinActive)
            {
                try
                {
                    var swHwnd = new IntPtr(iSwApp.IFrameObject().GetHWnd());
                    settingsPanel.Show(new Win32Window(swHwnd));
                }
                catch (Exception ex)
                {
                    Debug.Print("Could not set panel owner to SW main window, showing unowned: " + ex);
                    settingsPanel.Show();
                }
            }
            else
            {
                settingsPanel?.Hide();
            }

            // Opening gives a clean slate (the user explicitly opts into
            // whichever analysis they want each time); closing leaves
            // nothing behind in the viewport. Same reset either way.
            ClearAllAddinGraphics();
        }

        // Single place that clears every add-in graphic overlay --
        // zebra/isophote overlay and its face isolation, isocurves and
        // their degree/CV callouts, surface normals, and the G2
        // marker/comb/callout. Called when the panel opens, when it is
        // closed via the toolbar toggle, and when it is closed via its
        // own X button, so all three behave identically.
        private void ClearAllAddinGraphics()
        {
            OverlayEnabled = false;
            isolatedFacePersistRefs = null;
            IsIsolating = false;

            isocurveIsolatedFacePersistRefs = null;
            IsIsocurveIsolating = false;
            ClearIsocurveCallouts();

            if (NormalsShown)
            {
                ClearNormals();
                NormalsShown = false;
            }

            ClearG2Marker();
            ClearComb();
            ClearG2Callout();
            lastEdgeContinuityResult = null;

            settingsPanel?.SetG2ContinuityResult("No test run yet.");
            settingsPanel?.SyncOverlayButtonStates();
            settingsPanel?.SyncShowIsocurvesForSelectedButtonState();
            settingsPanel?.SyncNormalsButtonState();
            RequestOverlayRefresh();
        }

        // Enable/checked-state callback -- SW polls this to decide
        // whether to draw the button pressed. This is a plain int, not
        // an enum (there's no swCommandItemEnableState_e in the real
        // interop -- that was me conflating a third-party wrapper's
        // enum with the raw SW API). Documented meaning, unchanged since
        // the old AddMenuItem4/AddCommandItem2 days:
        //   0 = deselected + disabled
        //   1 = deselected + enabled (default)
        //   2 = selected + disabled
        //   3 = selected + enabled  <- pressed/checked look
        public int OnToggleAddinEnable()
        {
            return AddinActive ? 3 : 1;
        }

        #endregion

        #region Document event wiring

        private int OnActiveDocChangeNotify()
        {
            try
            {
                DetachFromActiveDoc();
                AttachToActiveDoc();
            }
            catch (Exception ex)
            {
                Debug.Print("OnActiveDocChangeNotify failed: " + ex);
            }
            return 0;
        }

        private void AttachToActiveDoc()
        {
            activeDoc = (ModelDoc2)iSwApp.ActiveDoc;
            if (activeDoc == null) return;

            glContextAppearsBroken = false;
            glContextRecoveryAttempts = 0;

            int docType = ((IModelDoc2)activeDoc).GetType();
            if ((swDocumentTypes_e)docType == swDocumentTypes_e.swDocPART)
            {
                activePart = (PartDoc)activeDoc;
                activePart.DimensionChangeNotify += OnDimensionChangeNotify;
                activePart.RegenNotify += OnRegenNotify;
                activePart.FileSaveNotify += OnFileSaveNotify;
                activePart.FileSaveAsNotify2 += OnFileSaveNotify;
            }

            // Normals reset to OFF on every document switch, matching
            // Overlay's behavior. Overlay achieves this for free since
            // it's redrawn every frame via a render hook that simply
            // stops firing for an inactive view -- normals are
            // persistent SW-native temp graphics instead, so getting the
            // same "off after switching" UX means actually clearing them,
            // not just flipping a flag. Doing that here (on attach, for
            // the document that's now genuinely active) rather than in
            // DetachFromActiveDoc is deliberate: clearing an OUTGOING
            // document's normals while it's no longer active doesn't
            // visually flush the removal (confirmed via testing) --
            // clearing the INCOMING, now-active document's own leftover
            // entry does, since ClearNormals's redraw call actually
            // takes effect on a document that's genuinely on-screen.
            ClearNormals();
            NormalsShown = false;
            settingsPanel?.SyncNormalsButtonState();

            InvalidateMeshCache();
        }

        private void DetachFromActiveDoc()
        {
            if (activePart != null)
            {
                activePart.DimensionChangeNotify -= OnDimensionChangeNotify;
                activePart.RegenNotify -= OnRegenNotify;
                activePart.FileSaveNotify -= OnFileSaveNotify;
                activePart.FileSaveAsNotify2 -= OnFileSaveNotify;
                activePart = null;
            }

            if (activeView != null)
            {
                try { activeView.GraphicsRenderPostNotify -= OnBufferSwapNotify; }
                catch { /* fine if it was never subscribed */ }
            }

            bodyMeshCache.Clear();
            bodyEdgeCache.Clear();
            bodyIsocurveCache.Clear();
            lastKnownBodyState.Clear();
            hasMeshFingerprint = false;

            isolatedFacePersistRefs = null;
            IsIsolating = false;

            isocurveIsolatedFacePersistRefs = null;
            IsIsocurveIsolating = false;
            ClearIsocurveCallouts();

            // Overlay no longer persists across document switches -- on a
            // large model, silently carrying an "on" state into a new
            // active doc/window risks an unexpected full-mesh build the
            // moment it becomes active, which is exactly the surprise
            // the Isolate/Show All toggle redesign is meant to avoid.
            OverlayEnabled = false;
            settingsPanel?.SyncOverlayButtonStates();

            // Isocurves are drawn the same way Overlay is -- a flag
            // checked every frame in OnBufferSwapNotify, not persistent
            // SW temp graphics like Normals -- so resetting here is safe
            // and sufficient: the render hook simply stops drawing them
            // for the view being left, no separate clear/redraw needed.
            // (isocurveIsolatedFacePersistRefs/IsIsocurveIsolating reset
            // above already covers this -- isocurve display is now
            // driven solely by that flag, per-face-only.)
            settingsPanel?.SyncShowIsocurvesForSelectedButtonState();

            // Normals also reset to OFF like Overlay, but deliberately
            // NOT here -- the doc being left is no longer active/visible
            // by this point, so clearing it here wouldn't actually flush
            // the removal from its screen (confirmed via testing).
            // AttachToActiveDoc does the actual clear+reset instead, for
            // whichever doc becomes active next, since that document is
            // the one genuinely on-screen at that point.

            activeDoc = null;
        }

        // Instant3D forces an immediate rebuild the moment a dimension
        // dialog edit is committed, rather than the normal manual
        // Ctrl+B flow -- compressing the timing window between the edit
        // and SW's own rebuild actually finishing. 150ms may not always
        // be enough headroom before RebuildMeshBuffer walks the model's
        // tessellation/faces; bumped up as a defensive buffer (not a
        // proven fix -- see open issue re: SLDWORKS.exe AV crash during
        // a dialog-committed dimension edit with Instant3D + Overlay on).
        private int OnDimensionChangeNotify(object displayDim)
        {
            try
            {
                DeferredRefresh(400);
            }
            catch (Exception ex)
            {
                Debug.Print("OnDimensionChangeNotify failed: " + ex);
            }
            return 0;
        }

        private int OnRegenNotify()
        {
            try
            {
                DeferredRefresh(400);
            }
            catch (Exception ex)
            {
                Debug.Print("OnRegenNotify failed: " + ex);
            }
            return 0;
        }

        private int OnFileSaveNotify(string FileName)
        {
            try
            {
                OverlayEnabled = false;
                settingsPanel?.SyncOverlayButtonStates();
            }
            catch (Exception ex)
            {
                Debug.Print("OnFileSaveNotify failed: " + ex);
            }
            return 0;
        }

        private System.Windows.Forms.Timer deferredRefreshTimer;

        // Single reusable timer, restarted rather than replaced on each
        // call -- previously this spun up a brand-new independent Timer
        // every time, so a single dimension edit under Instant3D (which
        // fires both DimensionChangeNotify and RegenNotify for the same
        // forced rebuild) could trigger two overlapping deferred
        // refreshes, each walking the model's geometry shortly after SW
        // itself just started a rebuild. Coalescing to one timer means
        // at most one deferred refresh fires per debounce window,
        // reducing (though not proving out) exposure to reading
        // geometry data while SW's own rebuild may still be settling.
        private void DeferredRefresh(int delayMs)
        {
            if (deferredRefreshTimer == null)
            {
                deferredRefreshTimer = new System.Windows.Forms.Timer();
                deferredRefreshTimer.Tick += (s, e) =>
                {
                    deferredRefreshTimer.Stop();
                    InvalidateMeshCache();
                    RequestOverlayRefresh();
                };
            }

            deferredRefreshTimer.Stop();
            deferredRefreshTimer.Interval = delayMs;
            deferredRefreshTimer.Start();
        }

        private void InvalidateMeshCache()
        {
            meshCacheDirty = true;
            isocurveCacheDirty = true;
        }
        private bool meshCacheDirty = true;
        private bool isocurveCacheDirty = true;

        #endregion

        #region Overlay refresh via BufferSwapNotify

        private void RequestOverlayRefresh()
        {
            try
            {
                if (activeDoc == null) return;

                var currentView = (ModelView)activeDoc.ActiveView;
                if (currentView == null) return;

                try
                {
                    if (activeView != null)
                    {
                        activeView.GraphicsRenderPostNotify -= OnBufferSwapNotify;
                    }
                }
                catch { /* fine if it was never subscribed on this view */ }

                activeView = currentView;
                activeView.GraphicsRenderPostNotify += OnBufferSwapNotify;
                activeView.GraphicsRedraw(null);
            }
            catch (Exception ex)
            {
                Debug.Print("RequestOverlayRefresh failed: " + ex);
            }
        }

        private int OnBufferSwapNotify()
        {
            try
            {
                // Primary guard, checked before anything else: if no GL
                // context is current on this thread at all, none of the
                // GL work below (mesh/isocurve buffer creation included,
                // not just drawing) can do anything meaningful, and
                // attempting it anyway is the leading suspect for open
                // issue #10's crash -- see the reasoning at the
                // GL.GetError()-based check further down for the full
                // chain of evidence. A missing context should be a
                // transient condition (plausibly SW briefly not having
                // made its context current on this thread during
                // Instant3D's forced-rebuild sequence), so simply
                // skipping this frame's work entirely and retrying next
                // frame is the correct response -- meshCacheDirty/
                // isocurveCacheDirty are deliberately left untouched so
                // the rebuild is attempted again as soon as a context is
                // available, rather than being silently dropped.
                if (GL.GetCurrentContext() == IntPtr.Zero)
                {
                    Debug.Print("OnBufferSwapNotify: no GL context current on this thread -- skipping this frame's work entirely (mesh/isocurve cache left dirty for retry).");
                    return 0;
                }

                if (OverlayEnabled && meshCacheDirty)
                {
                    RebuildMeshBuffer();
                    meshCacheDirty = false;
                }

                if (IsIsocurveIsolating && isocurveCacheDirty)
                {
                    RebuildIsocurveBuffer();
                    isocurveCacheDirty = false;
                }

                // Comb hairs and the G2 callout are no longer drawn here --
                // they're real SW temp-graphics objects (see RebuildComb /
                // RebuildG2Callout), which SW's own renderer draws, clips,
                // and redraws on its own, the same way ShowNormals /
                // DrawG2Marker already do.
                // Testing the leading hypothesis for open issue #10 (SW
                // crash during Instant3D forced rebuild): our GPU
                // resources (shaderProgram, meshBuffer, lineBuffer,
                // isocurveBuffer) are each cached ONCE and reused for
                // the entire document session. If SW recreates its own
                // GL context/surface as part of handling the forced
                // rebuild -- plausible for a big enough state change --
                // every handle we've cached becomes a dangling reference
                // into a context that no longer exists, and every
                // subsequent glUseProgram/glBindBuffer call using them
                // would generate exactly the GL_INVALID_OPERATION we
                // keep seeing, on every frame, forever -- matching what
                // repeated testing has shown. If that's right, this is
                // also a plausible path to the actual native crash:
                // feeding invalid handles into the driver on every frame
                // is exactly the kind of thing that can eventually
                // corrupt state badly enough to crash it.
                //
                // Rather than just detect-and-permanently-disable (the
                // previous approach, which stopped SW crashing but left
                // the add-in silently broken for good -- not an
                // acceptable outcome), this now attempts actual
                // self-healing: dispose the (already-dangling, so this
                // is just releasing our own bookkeeping) cached
                // resources and let the existing lazy-recreation logic
                // (shaderProgram == null, meshCacheDirty) naturally
                // rebuild everything fresh against whatever context is
                // now current. Capped at MAX_GL_CONTEXT_RECOVERY_ATTEMPTS
                // so a genuinely unrecoverable case doesn't retry forever
                // -- falls back to permanently disabling drawing (not
                // crashing SW) only once recovery itself keeps failing.
                if (!glContextAppearsBroken)
                {
                    uint entryErr = GL.GetError();
                    if (entryErr != 0)
                    {
                        bool shaderValid = shaderProgram?.IsValid ?? true; // true = n/a, not yet created
                        bool meshValid = meshBuffer?.IsValid ?? true;
                        bool lineValid = lineBuffer?.IsValid ?? true;
                        bool isoValid = isocurveBuffer?.IsValid ?? true;
                        IntPtr currentContext = GL.GetCurrentContext();
                        IntPtr currentDC = GL.GetCurrentDC();
                        Debug.Print("OnBufferSwapNotify: GL error 0x" + entryErr.ToString("X4") + " at hook entry, before any of our code ran this frame." +
                            " Current WGL context: 0x" + currentContext.ToString("X") + ", current DC: 0x" + currentDC.ToString("X") +
                            " (IntPtr.Zero for either is definitive proof no context is current on this thread right now)." +
                            " Cached resource validity in the CURRENT context -- shaderProgram:" + shaderValid + " meshBuffer:" + meshValid +
                            " lineBuffer:" + lineValid + " isocurveBuffer:" + isoValid +
                            " (meaningful only if a context IS current -- see above; note IsProgram/IsBuffer previously had a bug defaulting to true when the underlying wglGetProcAddress call itself failed, since fixed).");

                        if (glContextRecoveryAttempts < MAX_GL_CONTEXT_RECOVERY_ATTEMPTS)
                        {
                            glContextRecoveryAttempts++;
                            Debug.Print("OnBufferSwapNotify: attempting GL resource recovery (attempt " + glContextRecoveryAttempts +
                                "/" + MAX_GL_CONTEXT_RECOVERY_ATTEMPTS + ") -- disposing cached GL resources so they rebuild fresh next frame.");
                            DisposeAllCachedGLResources();
                            InvalidateMeshCache();
                        }
                        else
                        {
                            glContextAppearsBroken = true;
                            Debug.Print("OnBufferSwapNotify: GL context recovery attempts exhausted without success. Disabling further overlay/isocurve drawing for this document session.");
                        }

                        // Don't attempt to draw this same frame regardless
                        // of which branch above ran -- resources are
                        // either mid-recreation or drawing is disabled.
                    }
                    else
                    {
                        ApplyTopBandScissor();
                        DrawMesh();
                        DrawIsocurves();
                        RestoreScissor();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Print("OnBufferSwapNotify failed: " + ex);
            }

            return 0;
        }

        #endregion

        #region Mesh extraction (SW tessellation -> GPU VBO)

        private ulong lastMeshFingerprint;
        private bool hasMeshFingerprint;

        private readonly Dictionary<ulong, float[]> bodyMeshCache = new Dictionary<ulong, float[]>();
        private readonly Dictionary<ulong, float[]> bodyEdgeCache = new Dictionary<ulong, float[]>();
        private readonly Dictionary<ulong, (float[] alongU, float[] alongV)> bodyIsocurveCache =
            new Dictionary<ulong, (float[] alongU, float[] alongV)>();

        // isocurveBuffer holds the U-running segments first, then the
        // V-running ones, so DrawIsocurves can colour each family
        // separately (red = runs along U, green = runs along V) out of
        // the one buffer.
        private int isocurveAlongUVertexCount = 0;

        // Cheap pre-tessellation shortcut, gating the actual expensive
        // AppendBodyTessellation call itself -- not to be confused with
        // bodyMeshCache above, which is checked only AFTER tessellation
        // already ran (originally there to let multiple geometrically-
        // identical bodies share one cached result within a single
        // rebuild, e.g. an array of identical parts -- kept exactly as
        // is). This is a SEPARATE, additional layer: keyed by a body's
        // raw COM identity mapped to (a signature of exactly what
        // isolation-state/tolerance combination produced its last
        // result, that result's real content fingerprint). A match
        // means "the exact same body, in the exact same isolation and
        // tolerance state, as last successfully processed" -- safe by
        // construction: a false MISS here (raw identity changed, e.g.
        // after a real regen) just costs a harmless extra
        // re-tessellation, exactly like before this fix; there is no
        // path to a false HIT, since the only way identity can match a
        // prior observation is if it's genuinely the same live COM
        // object SW handed back, and SW's regen process creates fresh
        // objects rather than mutating geometry in place under an
        // unchanged reference.
        private readonly Dictionary<IntPtr, (ulong stateSignature, ulong contentFingerprint)> lastKnownBodyState =
            new Dictionary<IntPtr, (ulong, ulong)>();

        private List<byte[]> isolatedFacePersistRefs = null;

        // Mirrors isolatedFacePersistRefs' non-empty state as a public
        // static, the same pattern OverlayEnabled already uses, so the
        // (static) MeshSettingsPanel class can read current mode
        // directly for button-label sync without needing instance
        // access to the private isolatedFacePersistRefs field.
        public static bool IsIsolating = false;

        // Isocurve-only face isolation -- fully separate from the
        // overlay isolation above. Own persist-ref list, own resolved-
        // state flag, own callouts. A single button
        // ("Show Isocurves for Selected Faces") drives both the
        // isocurve display AND the per-face degree/CV callouts
        // together: click to show both for the current selection,
        // click again to hide both.
        private List<byte[]> isocurveIsolatedFacePersistRefs = null;
        public static bool IsIsocurveIsolating = false;

        private IsocurveCalloutHandler isocurveCalloutHandler;
        private readonly List<Callout> isocurveCallouts = new List<Callout>();

        private static IntPtr GetComIdentity(object comObject)
        {
            IntPtr ptr = Marshal.GetIUnknownForObject(comObject);
            Marshal.Release(ptr);
            return ptr;
        }

        private List<Face2> GetSelectedFaces()
        {
            var faces = new List<Face2>();
            if (activeDoc == null) return faces;

            var selMgr = (SelectionMgr)activeDoc.SelectionManager;
            int count = selMgr.GetSelectedObjectCount2(-1);

            for (int i = 1; i <= count; i++)
            {
                var selType = (swSelectType_e)selMgr.GetSelectedObjectType3(i, -1);
                if (selType == swSelectType_e.swSelFACES)
                {
                    if (selMgr.GetSelectedObject6(i, -1) is Face2 face)
                    {
                        faces.Add(face);
                    }
                }
            }
            return faces;
        }

        // "Isolate Selected Faces" is dual-purpose (replaces the removed
        // standalone Overlay on/off button): a selection present always
        // (re-)applies isolation and turns Overlay on, whatever its
        // current mode -- preserving the old workflow of repeatedly
        // narrowing a selection without needing to turn anything on
        // first. Clicking with nothing selected only acts as an off
        // switch, and only when Overlay is already on; with nothing
        // selected and Overlay already off, it's a no-op.
        private void IsolateSelectedFaces()
        {
            try
            {
                var selected = GetSelectedFaces();
                if (selected.Count == 0)
                {
                    if (OverlayEnabled)
                    {
                        OverlayEnabled = false;
                        settingsPanel?.SyncOverlayButtonStates();
                        RequestOverlayRefresh();
                    }
                    else
                    {
                        Debug.Print("IsolateSelectedFaces: nothing selected and Overlay already off, ignoring.");
                    }
                    return;
                }

                var modelExt = (IModelDocExtension)activeDoc.Extension;
                var persistRefs = new List<byte[]>();
                foreach (var face in selected)
                {
                    object refObj = modelExt.GetPersistReference3(face);
                    if (refObj is byte[] refBytes && refBytes.Length > 0)
                    {
                        persistRefs.Add(refBytes);
                    }
                    else
                    {
                        Debug.Print("IsolateSelectedFaces: could not get a persistent reference for one selected face, skipping it.");
                    }
                }

                if (persistRefs.Count == 0)
                {
                    Debug.Print("IsolateSelectedFaces: no persistent references obtained, ignoring.");
                    return;
                }

                isolatedFacePersistRefs = persistRefs;
                IsIsolating = true;
                OverlayEnabled = true;
                settingsPanel?.SyncOverlayButtonStates();

                InvalidateMeshCache();
                RequestOverlayRefresh();
            }
            catch (Exception ex)
            {
                Debug.Print("IsolateSelectedFaces failed: " + ex);
            }
        }

        // "Show All" is likewise dual-purpose. From off: turns Overlay
        // on showing every body (clearing any isolation). While
        // isolating: clears isolation but stays on (a mode switch, not
        // an off toggle -- this is the one case that doesn't turn
        // Overlay off, since "show everything instead" is what was
        // asked for). Already on and already showing all: turns off,
        // completing the toggle.
        private void ClearIsolation()
        {
            if (!OverlayEnabled)
            {
                OverlayEnabled = true;
                isolatedFacePersistRefs = null;
                IsIsolating = false;
                settingsPanel?.SyncOverlayButtonStates();
                InvalidateMeshCache();
                RequestOverlayRefresh();
            }
            else if (IsIsolating)
            {
                isolatedFacePersistRefs = null;
                IsIsolating = false;
                settingsPanel?.SyncOverlayButtonStates();
                InvalidateMeshCache();
                RequestOverlayRefresh();
            }
            else
            {
                OverlayEnabled = false;
                settingsPanel?.SyncOverlayButtonStates();
                RequestOverlayRefresh();
            }
        }

        private (List<IBody2> bodies, HashSet<IntPtr> faceIds, Dictionary<IntPtr, List<Face2>> facesByBody) ResolveIsolatedFaces()
        {
            var bodies = new List<IBody2>();
            var bodyIds = new HashSet<IntPtr>();
            var faceIds = new HashSet<IntPtr>();
            var facesByBody = new Dictionary<IntPtr, List<Face2>>();

            if (isolatedFacePersistRefs == null || activeDoc == null) return (bodies, faceIds, facesByBody);

            var modelExt = (IModelDocExtension)activeDoc.Extension;

            foreach (byte[] persistRef in isolatedFacePersistRefs)
            {
                object resolvedObj = null;
                try
                {
                    resolvedObj = modelExt.GetObjectByPersistReference3(persistRef, out int errorCode);
                    if (errorCode != 0) resolvedObj = null;
                }
                catch (Exception ex)
                {
                    Debug.Print("ResolveIsolatedFaces: GetObjectByPersistReference3 failed: " + ex);
                }

                if (!(resolvedObj is Face2 face)) continue;

                faceIds.Add(GetComIdentity(face));

                if (face.GetBody() is IBody2 body)
                {
                    IntPtr bodyId = GetComIdentity(body);
                    if (bodyIds.Add(bodyId))
                    {
                        bodies.Add(body);
                    }
                    // Also keep the live Face2 reference itself, grouped
                    // per body -- this is what lets tessellation itself
                    // be scoped to just the isolated faces (see
                    // AppendBodyTessellation's faceFilter param), rather
                    // than tessellating the whole body and throwing most
                    // of it away afterward via the RemoveAll filter
                    // below. Confirmed via SW's own documented
                    // IBody2::GetTessellation signature: its FaceList
                    // parameter exists specifically for this.
                    if (!facesByBody.TryGetValue(bodyId, out var list))
                    {
                        list = new List<Face2>();
                        facesByBody[bodyId] = list;
                    }
                    list.Add(face);
                }
            }

            return (bodies, faceIds, facesByBody);
        }

        // Isocurve-only isolation -- mirrors ResolveIsolatedFaces above
        // exactly, but reads from isocurveIsolatedFacePersistRefs
        // instead. Kept as a fully separate method (rather than a
        // shared helper parameterized on which list to use) so the two
        // isolation mechanisms stay genuinely independent, per
        // [stated]'s explicit requirement that this have nothing to do
        // with the overlay isolation.
        private (List<IBody2> bodies, HashSet<IntPtr> faceIds, Dictionary<IntPtr, List<Face2>> facesByBody) ResolveIsocurveIsolatedFaces()
        {
            var bodies = new List<IBody2>();
            var bodyIds = new HashSet<IntPtr>();
            var faceIds = new HashSet<IntPtr>();
            var facesByBody = new Dictionary<IntPtr, List<Face2>>();

            if (isocurveIsolatedFacePersistRefs == null || activeDoc == null) return (bodies, faceIds, facesByBody);

            var modelExt = (IModelDocExtension)activeDoc.Extension;

            foreach (byte[] persistRef in isocurveIsolatedFacePersistRefs)
            {
                object resolvedObj = null;
                try
                {
                    resolvedObj = modelExt.GetObjectByPersistReference3(persistRef, out int errorCode);
                    if (errorCode != 0) resolvedObj = null;
                }
                catch (Exception ex)
                {
                    Debug.Print("ResolveIsocurveIsolatedFaces: GetObjectByPersistReference3 failed: " + ex);
                }

                if (!(resolvedObj is Face2 face)) continue;

                faceIds.Add(GetComIdentity(face));

                if (face.GetBody() is IBody2 body)
                {
                    IntPtr bodyId = GetComIdentity(body);
                    if (bodyIds.Add(bodyId))
                    {
                        bodies.Add(body);
                    }
                    if (!facesByBody.TryGetValue(bodyId, out var list))
                    {
                        list = new List<Face2>();
                        facesByBody[bodyId] = list;
                    }
                    list.Add(face);
                }
            }

            return (bodies, faceIds, facesByBody);
        }

        // Single toggle button for isocurve display -- the only way to
        // show isocurves now (the earlier general Isocurves: ON/OFF
        // toggle, which showed isocurves on every face, has been
        // removed per [stated]'s request; this is per-face-only).
        // User selects face(s), clicks this -- isocurves AND per-face
        // degree/CV callouts appear for those faces; clicking again
        // (regardless of current selection) hides both.
        private void OnShowIsocurvesForSelectedFacesClicked()
        {
            try
            {
                if (IsIsocurveIsolating)
                {
                    isocurveIsolatedFacePersistRefs = null;
                    IsIsocurveIsolating = false;
                    ClearIsocurveCallouts();
                    isocurveCacheDirty = true;
                    settingsPanel?.SyncShowIsocurvesForSelectedButtonState();
                    RequestOverlayRefresh();
                    return;
                }

                if (activeDoc == null) return;

                var selected = GetSelectedFaces();
                if (selected.Count == 0)
                {
                    Debug.Print("OnShowIsocurvesForSelectedFacesClicked: nothing selected, ignoring.");
                    return;
                }

                var modelExt = (IModelDocExtension)activeDoc.Extension;
                var persistRefs = new List<byte[]>();
                foreach (var face in selected)
                {
                    object refObj = modelExt.GetPersistReference3(face);
                    if (refObj is byte[] refBytes && refBytes.Length > 0)
                    {
                        persistRefs.Add(refBytes);
                    }
                    else
                    {
                        Debug.Print("OnShowIsocurvesForSelectedFacesClicked: could not get a persistent reference for one selected face, skipping it.");
                    }
                }

                if (persistRefs.Count == 0)
                {
                    Debug.Print("OnShowIsocurvesForSelectedFacesClicked: no persistent references obtained, ignoring.");
                    return;
                }

                isocurveIsolatedFacePersistRefs = persistRefs;
                IsIsocurveIsolating = true;
                isocurveCacheDirty = true;

                BuildIsocurveCallouts(selected);
                settingsPanel?.SyncShowIsocurvesForSelectedButtonState();
                RequestOverlayRefresh();
            }
            catch (Exception ex)
            {
                Debug.Print("OnShowIsocurvesForSelectedFacesClicked failed: " + ex);
            }
        }

        private void RebuildMeshBuffer()
        {
            if (activeDoc == null)
            {
                meshBuffer?.Dispose();
                lineBuffer?.Dispose();
                meshBuffer = null;
                lineBuffer = null;
                return;
            }

            bool isolating = isolatedFacePersistRefs != null && isolatedFacePersistRefs.Count > 0;

            List<IBody2> isoBodies = null;
            HashSet<IntPtr> isoFaceIds = null;
            Dictionary<IntPtr, List<Face2>> isoFacesByBody = null;
            if (isolating)
            {
                var resolved = ResolveIsolatedFaces();
                isoBodies = resolved.bodies;
                isoFaceIds = resolved.faceIds;
                isoFacesByBody = resolved.facesByBody;
            }

            var partExt = (PartDoc)activeDoc;
            var allBodies = new List<object>();

            if (isolating)
            {
                allBodies.AddRange(isoBodies);
            }
            else
            {
                if (partExt.GetBodies2((int)swBodyType_e.swSolidBody, true) is object[] solidArr)
                    allBodies.AddRange(solidArr);
                if (partExt.GetBodies2((int)swBodyType_e.swSheetBody, true) is object[] sheetArr)
                    allBodies.AddRange(sheetArr);
            }

            var interleaved = new List<float>();
            var edgeLines = new List<float>();
            ulong combinedFingerprint = 14695981039346656037UL;
            bool anyBodyRecomputed = false;
            bool cancelled = false;
            int triangleCheckCounter = 0;

            foreach (object b in allBodies)
            {
                var body = (IBody2)b;

                Face2[] faceFilter = null;
                if (isolating && isoFacesByBody.TryGetValue(GetComIdentity(body), out var facesForThisBody))
                {
                    faceFilter = facesForThisBody.ToArray();
                }

                IntPtr bodyComIdentity = GetComIdentity(body);
                ulong stateSignature = ComputeStateSignature(faceFilter);

                if (lastKnownBodyState.TryGetValue(bodyComIdentity, out var lastState)
                    && lastState.stateSignature == stateSignature
                    && bodyMeshCache.TryGetValue(lastState.contentFingerprint, out float[] shortcutMeshData))
                {
                    // Same body, same isolation/tolerance state as last
                    // time we actually tessellated it -- skip
                    // AppendBodyTessellation entirely and reuse the
                    // already-cached result. This is what previously-
                    // seen states (e.g. toggling back to a prior
                    // isolation selection) now benefit from, rather than
                    // always re-paying full tessellation cost regardless
                    // of whether anything actually changed.
                    interleaved.AddRange(shortcutMeshData);
                    if (bodyEdgeCache.TryGetValue(lastState.contentFingerprint, out float[] shortcutEdgeData))
                    {
                        edgeLines.AddRange(shortcutEdgeData);
                    }
                    combinedFingerprint = CombineFingerprints(combinedFingerprint, lastState.contentFingerprint);
                    continue;
                }

                var bodyTriangles = new List<(Face2 face, double[][] pts, double[][] normals, int[] vertIds)>();
                AppendBodyTessellation(body, bodyTriangles, faceFilter);

                if (isolating)
                {
                    // Kept as a cheap safety net even though tessellation
                    // is now itself scoped via faceFilter above -- this
                    // list is already small post-scoping, so the cost is
                    // negligible, and it guards against any edge case
                    // where GetTessellation's FaceList doesn't perfectly
                    // restrict what comes back.
                    bodyTriangles.RemoveAll(tri => !isoFaceIds.Contains(GetComIdentity(tri.face)));
                }

                ulong bodyFingerprint = ComputeSkeletonFingerprint(bodyTriangles);
                combinedFingerprint = CombineFingerprints(combinedFingerprint, bodyFingerprint);
                lastKnownBodyState[bodyComIdentity] = (stateSignature, bodyFingerprint);

                if (bodyMeshCache.TryGetValue(bodyFingerprint, out float[] cachedBodyData))
                {
                    interleaved.AddRange(cachedBodyData);
                    if (bodyEdgeCache.TryGetValue(bodyFingerprint, out float[] cachedEdgeData))
                    {
                        edgeLines.AddRange(cachedEdgeData);
                    }
                    continue;
                }

                var bodyInterleaved = new List<float>();
                foreach (var tri in bodyTriangles)
                {
                    if (++triangleCheckCounter % 64 == 0 && EscKeyWatcher.IsEscapePressed())
                    {
                        cancelled = true;
                        break;
                    }
                    AppendTriangle(tri.face, tri.pts, tri.normals, bodyInterleaved);
                }

                if (cancelled) break;

                float[] bodyData = bodyInterleaved.ToArray();
                bodyMeshCache[bodyFingerprint] = bodyData;
                interleaved.AddRange(bodyData);

                float[] bodyEdgeData = ExtractBodyEdges(body, isolating ? isoFaceIds : null);
                bodyEdgeCache[bodyFingerprint] = bodyEdgeData;
                edgeLines.AddRange(bodyEdgeData);

                anyBodyRecomputed = true;
            }

            if (cancelled)
            {
                // Leave the previous meshBuffer/lineBuffer/cache state
                // untouched -- don't publish a half-built mesh. Auto-
                // disable Overlay: meshCacheDirty is still true, so
                // without this it would just restart on the very next
                // frame with the toggle still on.
                Debug.Print("RebuildMeshBuffer: cancelled via ESC, disabling overlay.");
                OverlayEnabled = false;
                settingsPanel?.SyncOverlayButtonStates();
                return;
            }

            if (!anyBodyRecomputed && hasMeshFingerprint && combinedFingerprint == lastMeshFingerprint && meshBuffer != null)
            {
                Debug.Print("RebuildMeshBuffer: nothing changed, skipping rebuild.");
                return;
            }
            lastMeshFingerprint = combinedFingerprint;
            hasMeshFingerprint = true;

            meshBuffer?.Dispose();
            lineBuffer?.Dispose();

            if (interleaved.Count == 0)
            {
                meshBuffer = null;
                lineBuffer = null;
                LastVisiblePolygonCount = 0;
                Debug.Print("RebuildMeshBuffer: interleaved.Count == 0, no vertices produced.");
                return;
            }

            meshBuffer = new MeshBuffer(interleaved.ToArray());
            lineBuffer = edgeLines.Count > 0 ? new LineBuffer(edgeLines.ToArray()) : null;
            LastVisiblePolygonCount = interleaved.Count / 6 / 3;
            Debug.Print("Mesh buffer rebuilt: " + (interleaved.Count / 6) + " vertices, " + (edgeLines.Count / 6) + " edge segments. (" +
                bodyMeshCache.Count + " distinct body shapes cached)" + (isolating ? " [ISOLATED: " + isoFaceIds.Count + " face(s) resolved]" : ""));
        }

        // Was a flat constant (12), then scaled with the old
        // SubdivisionLevel slider -- now that mesh density is fully
        // governed by MeshToleranceMM/ChordAngleDeg instead, this scales
        // inversely with MeshToleranceMM: tighter tolerance (smaller mm
        // value) means the user wants more precision everywhere, so the
        // drawn edge outline gets more segments too, not just the mesh
        // itself. Calibrated so the new default (0.1mm) gives 200
        // segments -- a density already confirmed to look good in
        // testing -- scaling up to a capped 2000 at very tight
        // tolerances and down to a floor of 12 at very loose ones.
        private static int EdgeSegments
        {
            get
            {
                double scaled = (0.1 / Math.Max(0.0001, MeshToleranceMM)) * 200.0;
                return (int)Math.Max(12, Math.Min(2000, scaled));
            }
        }

        private float[] ExtractBodyEdges(IBody2 body, HashSet<IntPtr> faceFilter)
        {
            var linePoints = new List<float>();

            object[] edgesObj = body.GetEdges() as object[];
            if (edgesObj == null) return linePoints.ToArray();

            foreach (object edgeObj in edgesObj)
            {
                var edge = (Edge)edgeObj;

                if (faceFilter != null)
                {
                    object[] adjFaces = edge.GetTwoAdjacentFaces2() as object[];

                    bool anyAdjacentIsolated = false;
                    if (adjFaces != null)
                    {
                        foreach (object adjFaceObj in adjFaces)
                        {
                            if (adjFaceObj != null && faceFilter.Contains(GetComIdentity(adjFaceObj)))
                            {
                                anyAdjacentIsolated = true;
                                break;
                            }
                        }
                    }

                    if (!anyAdjacentIsolated) continue;
                }

                var curve = edge.GetCurve() as Curve;
                if (curve == null) continue;

                curve.GetEndParams(out double tStart, out double tEnd, out bool startIsInfinite, out bool endIsInfinite);
                if (startIsInfinite || endIsInfinite) continue;

                int edgeSegments = EdgeSegments;
                double[] prevPoint = null;
                for (int i = 0; i <= edgeSegments; i++)
                {
                    double t = tStart + (tEnd - tStart) * i / edgeSegments;
                    double[] eval = curve.Evaluate(t) as double[];
                    if (eval == null || eval.Length < 3) continue;

                    if (prevPoint != null)
                    {
                        linePoints.Add((float)prevPoint[0]);
                        linePoints.Add((float)prevPoint[1]);
                        linePoints.Add((float)prevPoint[2]);
                        linePoints.Add((float)eval[0]);
                        linePoints.Add((float)eval[1]);
                        linePoints.Add((float)eval[2]);
                    }
                    prevPoint = eval;
                }
            }

            return linePoints.ToArray();
        }

        #region True-surface isocurve display

        private void RebuildIsocurveBuffer()
        {
            if (activeDoc == null)
            {
                isocurveBuffer?.Dispose();
                isocurveBuffer = null;
                return;
            }

            var partExt = (PartDoc)activeDoc;
            var allBodies = new List<object>();

            bool isolating = IsIsocurveIsolating && isocurveIsolatedFacePersistRefs != null && isocurveIsolatedFacePersistRefs.Count > 0;

            HashSet<IntPtr> isoFaceIds = null;
            Dictionary<IntPtr, List<Face2>> isoFacesByBody = null;

            if (isolating)
            {
                var resolved = ResolveIsocurveIsolatedFaces();
                isoFaceIds = resolved.faceIds;
                isoFacesByBody = resolved.facesByBody;
                allBodies.AddRange(resolved.bodies);
            }
            else
            {
                if (partExt.GetBodies2((int)swBodyType_e.swSolidBody, true) is object[] solidArr)
                    allBodies.AddRange(solidArr);
                if (partExt.GetBodies2((int)swBodyType_e.swSheetBody, true) is object[] sheetArr)
                    allBodies.AddRange(sheetArr);
            }

            var isoLinesAlongU = new List<float>();
            var isoLinesAlongV = new List<float>();

            foreach (object b in allBodies)
            {
                var body = (IBody2)b;

                Face2[] faceFilter = null;
                if (isolating && isoFacesByBody.TryGetValue(GetComIdentity(body), out var facesForThisBody))
                {
                    faceFilter = facesForThisBody.ToArray();
                }

                var bodyTriangles = new List<(Face2 face, double[][] pts, double[][] normals, int[] vertIds)>();
                AppendBodyTessellation(body, bodyTriangles, faceFilter);
                ulong bodyFingerprint = ComputeSkeletonFingerprint(bodyTriangles);

                if (isolating)
                {
                    // Fold the face-selection state into the cache key --
                    // reuses the same state-signature helper the overlay
                    // isolation already relies on, so a change in WHICH
                    // faces are isocurve-isolated (same body) doesn't hit
                    // a stale cache entry left over from a prior selection.
                    bodyFingerprint = CombineFingerprints(bodyFingerprint, ComputeStateSignature(faceFilter));
                }

                if (bodyIsocurveCache.TryGetValue(bodyFingerprint, out var cachedIso))
                {
                    isoLinesAlongU.AddRange(cachedIso.alongU);
                    isoLinesAlongV.AddRange(cachedIso.alongV);
                    continue;
                }

                var bodyIso = ExtractBodyIsocurves(body, isolating ? isoFaceIds : null);
                bodyIsocurveCache[bodyFingerprint] = bodyIso;
                isoLinesAlongU.AddRange(bodyIso.alongU);
                isoLinesAlongV.AddRange(bodyIso.alongV);
            }

            isocurveAlongUVertexCount = isoLinesAlongU.Count / 3;
            var isoLines = new List<float>(isoLinesAlongU.Count + isoLinesAlongV.Count);
            isoLines.AddRange(isoLinesAlongU);
            isoLines.AddRange(isoLinesAlongV);

            isocurveBuffer?.Dispose();
            isocurveBuffer = isoLines.Count > 0 ? new LineBuffer(isoLines.ToArray()) : null;
            Debug.Print("Isocurve buffer rebuilt: " + (isoLines.Count / 6) + " line segments." +
                (isolating ? " [ISOLATED: " + isoFaceIds.Count + " face(s)]" : ""));
        }

        private (float[] alongU, float[] alongV) ExtractBodyIsocurves(IBody2 body, HashSet<IntPtr> faceFilter = null)
        {
            var alongU = new List<float>();
            var alongV = new List<float>();

            object[] faces = (object[])body.GetFaces();
            if (faces == null) return (alongU.ToArray(), alongV.ToArray());

            foreach (object fObj in faces)
            {
                var face = (Face2)fObj;
                if (faceFilter != null && !faceFilter.Contains(GetComIdentity(face))) continue;
                AppendFaceIsocurves(face, alongU, alongV);
            }

            return (alongU.ToArray(), alongV.ToArray());
        }

        private void AppendFaceIsocurves(Face2 face, List<float> alongU, List<float> alongV)
        {
            var surf = (Surface)face.GetSurface();
            if (surf == null) return;

            Debug.Print("Isocurve face: IsCylinder=" + surf.IsCylinder() + " IsSphere=" + surf.IsSphere() +
                " IsCone=" + surf.IsCone() + " IsPlane=" + surf.IsPlane() + " IsTorus=" + surf.IsTorus() +
                " IsSwept=" + surf.IsSwept() + " IsBlending=" + surf.IsBlending());

            BSurfParamData bsurf;
            try
            {
                var vp0 = (SurfaceParameterizationData)surf.Parameterization2();
                bsurf = (BSurfParamData)surf.GetBSurfParams3(false, false, vp0, 0.01, out bool sense);
            }
            catch (Exception ex)
            {
                Debug.Print("AppendFaceIsocurves: GetBSurfParams3 failed: " + ex);
                return;
            }
            if (bsurf == null) { Debug.Print("Isocurve face: bsurf is null"); return; }

            var rawUKnots = bsurf.UKnots as double[];
            var rawVKnots = bsurf.VKnots as double[];
            Debug.Print("Isocurve face: raw UKnots count=" + (rawUKnots?.Length ?? -1) + " raw VKnots count=" + (rawVKnots?.Length ?? -1));

            var uKnots = DedupeKnots(rawUKnots);
            var vKnots = DedupeKnots(rawVKnots);
            Debug.Print("Isocurve face: unique U=" + uKnots.Count + " unique V=" + vKnots.Count);

            // Colour is by which knot vector the curve sits on, so the
            // NUMBER of lines of a colour tracks that direction's CV
            // count in the callout: the curves at the U knots are the
            // "U (red)" family, the curves at the V knots are "V (green)".
            // (Each one physically runs in the other direction.) The
            // list names alongU/alongV are just "red"/"green" here.
            foreach (double u in uKnots) AppendIsocurveLine(surf, face, false, u, alongU);
            foreach (double v in vKnots) AppendIsocurveLine(surf, face, true, v, alongV);
        }

        private static List<double> DedupeKnots(double[] knots)
        {
            var result = new List<double>();
            if (knots == null) return result;
            foreach (double k in knots)
            {
                if (result.Count == 0 || Math.Abs(k - result[result.Count - 1]) > 1e-9)
                {
                    result.Add(k);
                }
            }

            if (result.Count == 2)
            {
                result.Insert(1, (result[0] + result[1]) / 2.0);
            }

            return result;
        }

        private const int ISOCURVE_SEGMENTS = 40;
        private const double ISOCURVE_TRIM_TOL_SQ = 0.0000001;

        private void AppendIsocurveLine(Surface surf, Face2 face, bool isV, double uvValue, List<float> linePoints)
        {
            Curve isoCurve;
            try
            {
                isoCurve = (Curve)surf.IMakeIsoCurve(isV, uvValue);
            }
            catch (Exception ex)
            {
                Debug.Print("IMakeIsoCurve failed: " + ex);
                return;
            }
            if (isoCurve == null) return;

            double[] uvBounds = (double[])face.GetUVBounds();
            double tStart = isV ? uvBounds[0] : uvBounds[2];
            double tEnd = isV ? uvBounds[1] : uvBounds[3];

            double[] prevPoint = null;
            bool prevInside = false;

            for (int i = 0; i <= ISOCURVE_SEGMENTS; i++)
            {
                double t = tStart + (tEnd - tStart) * i / ISOCURVE_SEGMENTS;
                double[] eval = isoCurve.Evaluate(t) as double[];
                if (eval == null || eval.Length < 3) { prevPoint = null; prevInside = false; continue; }

                double[] closest = (double[])face.GetClosestPointOn(eval[0], eval[1], eval[2]);
                double dx = closest[0] - eval[0], dy = closest[1] - eval[1], dz = closest[2] - eval[2];
                bool inside = (dx * dx + dy * dy + dz * dz) < ISOCURVE_TRIM_TOL_SQ;

                if (inside && prevInside && prevPoint != null)
                {
                    linePoints.Add((float)prevPoint[0]);
                    linePoints.Add((float)prevPoint[1]);
                    linePoints.Add((float)prevPoint[2]);
                    linePoints.Add((float)eval[0]);
                    linePoints.Add((float)eval[1]);
                    linePoints.Add((float)eval[2]);
                }

                prevPoint = eval;
                prevInside = inside;
            }
        }

        // Builds one native "Absolute/Relative"-style callout per
        // selected face, showing that face's surface degree and CV
        // count for each of U and V (one row per direction, per
        // [stated]'s spec). Mirrors RebuildG2Callout's exact
        // ISwCalloutHandler/CreateCallout pattern -- see that method
        // for the "style" parameter caveat and the ValueInactive-based
        // read-only setup.
        //
        // UOrder/VOrder/ControlPointColumnCount/ControlPointRowCount
        // confirmed against SW's own official GetBSurfParams3 doc
        // example (only UKnots/VKnots had been exercised elsewhere in
        // this codebase, in AppendFaceIsocurves above, before this).
        private void BuildIsocurveCallouts(List<Face2> faces)
        {
            ClearIsocurveCallouts();

            if (activeDoc == null || activeView == null) return;
            if (isocurveCalloutHandler == null) isocurveCalloutHandler = new IsocurveCalloutHandler();

            foreach (var face in faces)
            {
                var surf = (Surface)face.GetSurface();
                if (surf == null) continue;

                BSurfParamData bsurf;
                try
                {
                    var vp0 = (SurfaceParameterizationData)surf.Parameterization2();
                    bsurf = (BSurfParamData)surf.GetBSurfParams3(false, false, vp0, 0.01, out bool sense);
                }
                catch (Exception ex)
                {
                    Debug.Print("BuildIsocurveCallouts: GetBSurfParams3 failed: " + ex);
                    continue;
                }
                if (bsurf == null) continue;

                int uDegree = bsurf.UOrder - 1;
                int vDegree = bsurf.VOrder - 1;
                // Confirmed against SW's own official GetBSurfParams3
                // example (VB/C#): "# UKnots = ControlPointColumnCount +
                // UOrder" and "# VKnots = ControlPointRowCount + VOrder"
                // -- i.e. the U-direction control point count is
                // ControlPointColumnCount, and V-direction is
                // ControlPointRowCount. There is no UCount/VCount member.
                int uCvCount = bsurf.ControlPointColumnCount;
                int vCvCount = bsurf.ControlPointRowCount;

                var callout = (Callout)activeView.CreateCallout(2, isocurveCalloutHandler);
                if (callout == null)
                {
                    Debug.Print("BuildIsocurveCallouts: CreateCallout returned null for one face.");
                    continue;
                }

                callout.Label2[0] = "U (red)";
                callout.Value[0] = "Degree " + uDegree + ", CVs " + uCvCount;
                callout.ValueInactive[0] = true;

                callout.Label2[1] = "V (green)";
                callout.Value[1] = "Degree " + vDegree + ", CVs " + vCvCount;
                callout.ValueInactive[1] = true;

                double[] uvBounds = (double[])face.GetUVBounds();
                double uMid = (uvBounds[0] + uvBounds[1]) / 2.0;
                double vMid = (uvBounds[2] + uvBounds[3]) / 2.0;
                double[] eval = (double[])surf.Evaluate(uMid, vMid, 0, 0);

                callout.SetTargetPoint(0, eval[0], eval[1], eval[2]);
                callout.SetTargetPoint(1, eval[0], eval[1], eval[2]);

                callout.MultipleLeaders = false;
                callout.Display(true);

                isocurveCallouts.Add(callout);
            }

            RequestOverlayRefresh();
        }

        private void ClearIsocurveCallouts()
        {
            foreach (var callout in isocurveCallouts)
            {
                try
                {
                    callout.Display(false);
                    Marshal.ReleaseComObject(callout);
                }
                catch (Exception ex)
                {
                    Debug.Print("ReleaseComObject on isocurve callout failed: " + ex);
                }
            }
            isocurveCallouts.Clear();
        }

        #endregion

        #region G2 (curvature) edge-continuity check

        private struct EdgeContinuitySample
        {
            public double t;
            public double[] point;
            public double[] normal1;
            public double kappa1;
            public double kappa2;
            public double absoluteDeviation;
            public double relativeDeviation;
        }

        private class EdgeContinuityResult
        {
            public List<EdgeContinuitySample> samples = new List<EdgeContinuitySample>();
            public bool hasWorstSample = false;
            public EdgeContinuitySample worstSample;
            public string errorMessage = null;
        }

        // Density -- live-adjustable via the settings panel (matches
        // Rhino's own Density field). Was a private const int = 20;
        // changing this only affects the NEXT TestEdgeContinuity run,
        // since there's no held live Edge COM reference to recompute
        // against immediately.
        public static int EdgeContinuitySampleCount = 50;

        private static EdgeContinuityResult ComputeEdgeContinuity(Edge edge)
        {
            var result = new EdgeContinuityResult();

            object[] adjFaces = edge.GetTwoAdjacentFaces2() as object[];
            if (adjFaces == null || adjFaces.Length < 2 || adjFaces[0] == null || adjFaces[1] == null)
            {
                result.errorMessage = "Selected edge does not have two adjacent faces (open boundary).";
                return result;
            }

            var face1 = (Face2)adjFaces[0];
            var face2 = (Face2)adjFaces[1];
            var surf1 = (Surface)face1.GetSurface();
            var surf2 = (Surface)face2.GetSurface();

            var curve = edge.GetCurve() as Curve;
            if (curve == null)
            {
                result.errorMessage = "Could not get the underlying curve for this edge.";
                return result;
            }

            curve.GetEndParams(out double tStart, out double tEnd, out bool startIsInfinite, out bool endIsInfinite);

            if (startIsInfinite || endIsInfinite)
            {
                // Closed/periodic curve (e.g. a full circular edge) --
                // GetEndParams can't describe a bounded range for these,
                // so fall back to the edge's own trim bounds instead.
                // Only used for this case: on ordinary bounded edges,
                // GetCurveParams2 has shown unreliable parameterization
                // on complex (spline/blend) curves, so the normally-
                // correct GetEndParams path is trusted for everything else.
                double[] curveParams = (double[])edge.GetCurveParams2();
                if (curveParams == null || curveParams.Length < 8)
                {
                    result.errorMessage = "Could not get parameter range for this closed edge.";
                    return result;
                }
                tStart = curveParams[6];
                tEnd = curveParams[7];
            }

            double eps = (tEnd - tStart) * 0.001;

            for (int i = 0; i <= EdgeContinuitySampleCount; i++)
            {
                double t = tStart + (tEnd - tStart) * i / EdgeContinuitySampleCount;
                double tPlus = Math.Min(t + eps, tEnd);
                double tMinus = Math.Max(t - eps, tStart);

                double[] p = EvalCurvePoint(curve, t);
                double[] pPlus = EvalCurvePoint(curve, tPlus);
                double[] pMinus = EvalCurvePoint(curve, tMinus);
                if (p == null || pPlus == null || pMinus == null) continue;

                double[] tangent = Normalize3(Subtract3(pPlus, pMinus));
                if (tangent == null) continue;

                bool ok1 = TryComputeCurvatureAtPoint(face1, surf1, p, tangent, out double kappa1, out double[] d1, out double[] normal1);
                bool ok2 = TryComputeCurvatureAtPoint(face2, surf2, p, tangent, out double kappa2, out double[] d2, out double[] normal2);
                if (!ok1 || !ok2) continue;

                var sample = new EdgeContinuitySample();
                sample.t = t;
                sample.point = p;
                sample.normal1 = normal1;
                sample.kappa1 = kappa1;
                sample.kappa2 = kappa2;
                sample.absoluteDeviation = kappa1 - kappa2;

                double r1 = (Math.Abs(kappa1) < 1e-9) ? double.PositiveInfinity : 1.0 / kappa1;
                double r2 = (Math.Abs(kappa2) < 1e-9) ? double.PositiveInfinity : 1.0 / kappa2;
                bool r1Inf = double.IsInfinity(r1);
                bool r2Inf = double.IsInfinity(r2);
                if (r1Inf && r2Inf)
                {
                    sample.relativeDeviation = 0.0;
                }
                else if (r1Inf)
                {
                    sample.relativeDeviation = 1.0;
                }
                else if (r2Inf)
                {
                    sample.relativeDeviation = -1.0;
                }
                else
                {
                    sample.relativeDeviation = (r1 - r2) / (r1 + r2);
                }

                result.samples.Add(sample);

                if (!result.hasWorstSample || Math.Abs(sample.absoluteDeviation) > Math.Abs(result.worstSample.absoluteDeviation))
                {
                    result.worstSample = sample;
                    result.hasWorstSample = true;
                }
            }

            return result;
        }

        private static bool TryComputeCurvatureAtPoint(Face2 face, Surface surf, double[] point, double[] tangent, out double kappa, out double[] dOut, out double[] normalOut)
        {
            kappa = 0.0;
            dOut = null;
            normalOut = null;

            double[] uvResult = (double[])face.GetClosestPointOn(point[0], point[1], point[2]);
            double u = uvResult[3];
            double v = uvResult[4];

            double[] evalResult = (double[])surf.Evaluate(u, v, 2, 2);

            double[] Su = { evalResult[3], evalResult[4], evalResult[5] };
            double[] Suu = { evalResult[6], evalResult[7], evalResult[8] };
            double[] Sv = { evalResult[9], evalResult[10], evalResult[11] };
            double[] Suv = { evalResult[12], evalResult[13], evalResult[14] };
            double[] Svv = { evalResult[18], evalResult[19], evalResult[20] };
            double[] n = { evalResult[27], evalResult[28], evalResult[29] };

            if (face.FaceInSurfaceSense())
            {
                n[0] = -n[0]; n[1] = -n[1]; n[2] = -n[2];
            }

            double[] d = Cross3(n, tangent);

            double E = Dot3(Su, Su);
            double F = Dot3(Su, Sv);
            double G = Dot3(Sv, Sv);
            double L = Dot3(Suu, n);
            double M = Dot3(Suv, n);
            double N = Dot3(Svv, n);

            double dSu = Dot3(d, Su);
            double dSv = Dot3(d, Sv);
            double denom = E * G - F * F;
            if (Math.Abs(denom) < 1e-12) return false;

            double a = (dSu * G - dSv * F) / denom;
            double b = (dSv * E - dSu * F) / denom;

            double numer = L * a * a + 2 * M * a * b + N * b * b;
            double denom2 = E * a * a + 2 * F * a * b + G * b * b;
            if (Math.Abs(denom2) < 1e-12) return false;

            kappa = numer / denom2;
            dOut = d;
            normalOut = n;
            return true;
        }

        private static double[] EvalCurvePoint(Curve curve, double t)
        {
            double[] r = curve.Evaluate(t) as double[];
            if (r == null || r.Length < 3) return null;
            return new double[] { r[0], r[1], r[2] };
        }

        private static double[] Subtract3(double[] a, double[] b)
        {
            return new double[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        }

        private static double[] Normalize3(double[] v)
        {
            double mag = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (mag < 1e-9) return null;
            return new double[] { v[0] / mag, v[1] / mag, v[2] / mag };
        }

        private static double[] Cross3(double[] a, double[] b)
        {
            return new double[]
            {
                a[1] * b[2] - a[2] * b[1],
                a[2] * b[0] - a[0] * b[2],
                a[0] * b[1] - a[1] * b[0]
            };
        }

        private static double Dot3(double[] a, double[] b)
        {
            return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        }

        private void TestEdgeContinuity()
        {
            try
            {
                ClearG2Marker();
                ClearComb();
                ClearG2Callout();
                lastEdgeContinuityResult = null;

                if (activeDoc == null)
                {
                    Debug.Print("[G2] No active document.");
                    settingsPanel?.SetG2ContinuityResult("No active document.");
                    return;
                }

                var selMgr = (SelectionMgr)activeDoc.SelectionManager;

                if (selMgr.GetSelectedObjectCount2(-1) == 0)
                {
                    Debug.Print("[G2] No selection -- pick an edge first.");
                    settingsPanel?.SetG2ContinuityResult("Select an edge first.");
                    return;
                }

                var selType = (swSelectType_e)selMgr.GetSelectedObjectType3(1, -1);
                if (selType != swSelectType_e.swSelEDGES && selType != swSelectType_e.swSelREFEDGES)
                {
                    Debug.Print("[G2] Selection is not an edge (type=" + selType + ").");
                    settingsPanel?.SetG2ContinuityResult("Selection is not an edge.");
                    return;
                }

                var edge = (Edge)selMgr.GetSelectedObject6(1, -1);

                EdgeContinuityResult result = ComputeEdgeContinuity(edge);

                if (result.errorMessage != null)
                {
                    Debug.Print("[G2] " + result.errorMessage);
                    settingsPanel?.SetG2ContinuityResult(result.errorMessage);
                    return;
                }

                Debug.Print("[G2] Sampled " + result.samples.Count + " of " + (EdgeContinuitySampleCount + 1) + " points along edge.");

                if (!result.hasWorstSample)
                {
                    Debug.Print("[G2] No valid samples (all degenerate) -- cannot assess continuity.");
                    settingsPanel?.SetG2ContinuityResult("No valid samples (all degenerate).");
                    return;
                }

                EdgeContinuitySample w = result.worstSample;

                // Display conversion: SW's API works in meters internally,
                // but users read curvature in 1/mm (matches Rhino's own
                // displayed units, confirmed via the earlier cross-checks).
                // relativeDeviation is already dimensionless -- no conversion.
                double kappa1PerMM = w.kappa1 / 1000.0;
                double kappa2PerMM = w.kappa2 / 1000.0;
                double absDevPerMM = w.absoluteDeviation / 1000.0;

                Debug.Print("[G2] Worst deviation at t=" + w.t.ToString("F4"));
                Debug.Print("[G2]   kappa1 = " + kappa1PerMM.ToString("F6") + " /mm, kappa2 = " + kappa2PerMM.ToString("F6") + " /mm");
                Debug.Print("[G2]   absolute deviation (1/R1 - 1/R2) = " + absDevPerMM.ToString("F6") + " /mm");
                Debug.Print("[G2]   relative deviation ((R1-R2)/(R1+R2)) = " + w.relativeDeviation.ToString("F6"));
                Debug.Print("[G2]   point (model, m) = (" + w.point[0].ToString("F5") + ", " + w.point[1].ToString("F5") + ", " + w.point[2].ToString("F5") + ")");

                settingsPanel?.SetG2ContinuityResult("Test complete -- see callout in graphics area.");

                lastEdgeContinuityResult = result;
                RebuildComb();

                DrawG2Marker(w.point);
                RebuildG2Callout(w);
            }
            catch (Exception ex)
            {
                Debug.Print("TestEdgeContinuity failed: " + ex);
                settingsPanel?.SetG2ContinuityResult("Error: " + ex.Message);
            }
            finally
            {
                try
                {
                    activeView.GraphicsRedraw(null);
                }
                catch (Exception ex)
                {
                    Debug.Print("GraphicsRedraw failed: " + ex.Message);
                }
            }
        }

        private const double G2_MARKER_HALF_LENGTH_MM = 1.5;

        private readonly List<IBody2> g2MarkerWireBodies = new List<IBody2>();

        private void DrawG2Marker(double[] point)
        {
            try
            {
                if (activeDoc == null) return;

                var modeler = (Modeler)iSwApp.GetModeler();
                double half = G2_MARKER_HALF_LENGTH_MM / 1000.0;

                DrawMarkerArm(modeler, point, new double[] { 1, 0, 0 }, half);
                DrawMarkerArm(modeler, point, new double[] { 0, 1, 0 }, half);
                DrawMarkerArm(modeler, point, new double[] { 0, 0, 1 }, half);

                RequestOverlayRefresh();
            }
            catch (Exception ex)
            {
                Debug.Print("DrawG2Marker failed: " + ex);
            }
        }

        private void DrawMarkerArm(Modeler modeler, double[] center, double[] axis, double half)
        {
            double x1 = center[0] - axis[0] * half, y1 = center[1] - axis[1] * half, z1 = center[2] - axis[2] * half;
            double x2 = center[0] + axis[0] * half, y2 = center[1] + axis[1] * half, z2 = center[2] + axis[2] * half;

            ICurve line = (ICurve)modeler.CreateLine(new[] { x1, y1, z1 }, axis);
            if (line == null) return;

            ICurve trimmed = (ICurve)line.CreateTrimmedCurve2(x1, y1, z1, x2, y2, z2);
            if (trimmed == null) return;

            IBody2 wireBody = (IBody2)trimmed.CreateWireBody();
            if (wireBody == null) return;

            int color = 255 | (0 << 8) | (255 << 16);
            wireBody.Display3(activeDoc, color, (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);

            g2MarkerWireBodies.Add(wireBody);
        }

        private void ClearG2Marker()
        {
            foreach (var body in g2MarkerWireBodies)
            {
                try
                {
                    Marshal.ReleaseComObject(body);
                }
                catch (Exception ex)
                {
                    Debug.Print("ReleaseComObject on G2 marker wire body failed: " + ex);
                }
            }
            g2MarkerWireBodies.Clear();
        }

        // Native on-screen callout at the worst-deviation point, showing
        // absolute/relative curvature deviation directly in the graphics
        // area (matches Rhino's EdgeContinuity display). Uses SW's
        // ModelView::CreateCallout API, confirmed via the official
        // "Create Model View Callouts" and "Create Multi-row Callouts"
        // examples.
        //
        // ISwCalloutHandler has exactly ONE member (OnStringValueChanged,
        // confirmed via the interop assembly's decompiled interface and
        // the official Members page) -- it exists only because
        // CreateCallout requires a non-null handler; our callout is
        // read-only (ValueInactive on every row) so it's a pure no-op.
        //
        // NOTE: the CreateCallout "style" parameter's exact meaning isn't
        // confirmed from documentation (only two data points were
        // available: 1 for a single-row example, 4 for a four-row
        // example) -- 2 is a reasonable guess for our two rows
        // (absolute + relative) but genuinely needs a live test; if it
        // doesn't render both rows correctly, try 1 or 4 instead.
        private G2CalloutHandler g2CalloutHandler;
        private readonly List<Callout> g2Callouts = new List<Callout>();

        private void RebuildG2Callout(EdgeContinuitySample w)
        {
            ClearG2Callout();

            try
            {
                if (activeDoc == null || activeView == null) return;
                if (g2CalloutHandler == null) g2CalloutHandler = new G2CalloutHandler();

                var callout = (Callout)activeView.CreateCallout(2, g2CalloutHandler);
                if (callout == null)
                {
                    Debug.Print("RebuildG2Callout: CreateCallout returned null.");
                    return;
                }

                double absDevPerMM = w.absoluteDeviation / 1000.0;

                callout.Label2[0] = "Absolute";
                callout.Value[0] = absDevPerMM.ToString("F4") + " /mm";
                callout.ValueInactive[0] = true;

                callout.Label2[1] = "Relative";
                callout.Value[1] = (w.relativeDeviation * 100.0).ToString("F2") + "%";
                callout.ValueInactive[1] = true;

                callout.SetTargetPoint(0, w.point[0], w.point[1], w.point[2]);
                callout.SetTargetPoint(1, w.point[0], w.point[1], w.point[2]);

                callout.MultipleLeaders = false;
                callout.Display(true);

                g2Callouts.Add(callout);

                RequestOverlayRefresh();
            }
            catch (Exception ex)
            {
                Debug.Print("RebuildG2Callout failed: " + ex);
            }
        }

        private void ClearG2Callout()
        {
            foreach (var callout in g2Callouts)
            {
                try
                {
                    callout.Display(false);
                    Marshal.ReleaseComObject(callout);
                }
                catch (Exception ex)
                {
                    Debug.Print("ReleaseComObject on G2 callout failed: " + ex);
                }
            }
            g2Callouts.Clear();
        }

        // Comb tuning fields. CombToleranceAbsPerMM replaces the old
        // CombTolerancePercent/CombMaxPercent pair: Rhino's own
        // "Curvature tolerance" field is an ABSOLUTE curvature-deviation
        // threshold in 1/mm (e.g. 0.002), not a percentage -- confirmed
        // against the absolute-deviation numbers from the Rhino
        // cross-checks (0.002, 0.029). There is no separate "max" tier:
        // max/blue marks only the single worst sample on the edge.
        public static double CombScale = 500.0;
        public static double CombToleranceAbsPerMM = 0.002;

        private const double COMB_LENGTH_CLAMP_MM = 20.0;

        private EdgeContinuityResult lastEdgeContinuityResult;

        // Comb hairs are drawn as real SW temp-graphics wire bodies
        // (same technique as ShowNormals/DrawG2Marker), NOT via the GL
        // render hook. This sidesteps a whole class of camera-relative
        // problems the GL version had (near-plane clipping during orbit,
        // staleness until the hook happened to fire) since SW's own
        // renderer owns drawing, clipping, and redraw-on-camera-change
        // for temp graphics -- confirmed via ShowNormals already
        // rendering through occluding geometry the same way the comb is
        // meant to.
        private readonly List<IBody2> combWireBodies = new List<IBody2>();

        private void ClearComb()
        {
            foreach (var body in combWireBodies)
            {
                try
                {
                    Marshal.ReleaseComObject(body);
                }
                catch (Exception ex)
                {
                    Debug.Print("ReleaseComObject on comb wire body failed: " + ex);
                }
            }
            combWireBodies.Clear();
        }

        private void RebuildComb()
        {
            ClearComb();

            if (activeDoc == null) return;
            if (lastEdgeContinuityResult == null || !lastEdgeContinuityResult.hasWorstSample) return;

            var modeler = (Modeler)iSwApp.GetModeler();

            foreach (var sample in lastEdgeContinuityResult.samples)
            {
                double deviationPerMM = Math.Abs(sample.absoluteDeviation) / 1000.0; // 1/m -> 1/mm
                double hairLengthMM = deviationPerMM * CombScale;
                if (hairLengthMM > COMB_LENGTH_CLAMP_MM) hairLengthMM = COMB_LENGTH_CLAMP_MM;
                double hairLengthM = hairLengthMM / 1000.0;
                if (hairLengthM < 1e-9) continue; // zero-length hair, nothing to draw

                double[] p0 = sample.point;
                double[] p1 =
                {
                    p0[0] + sample.normal1[0] * hairLengthM,
                    p0[1] + sample.normal1[1] * hairLengthM,
                    p0[2] + sample.normal1[2] * hairLengthM
                };

                // Matches Rhino's real scheme: good/bad is a plain 2-way
                // split by absolute curvature-deviation tolerance;
                // max/blue is NOT a second tolerance tier -- it marks
                // only the single worst sample on the whole edge,
                // overriding whichever tolerance bucket it'd land in.
                bool isWorst = sample.t == lastEdgeContinuityResult.worstSample.t;
                int bucket = isWorst ? 2 : (deviationPerMM > CombToleranceAbsPerMM) ? 1 : 0;

                DrawCombHairSegment(modeler, p0, p1, GetCombColor(bucket));
            }

            RequestOverlayRefresh();
        }

        private void DrawCombHairSegment(Modeler modeler, double[] p0, double[] p1, int color)
        {
            double dx = p1[0] - p0[0], dy = p1[1] - p0[1], dz = p1[2] - p0[2];

            ICurve line = (ICurve)modeler.CreateLine(p0, new[] { dx, dy, dz });
            if (line == null) return;

            ICurve trimmed = (ICurve)line.CreateTrimmedCurve2(p0[0], p0[1], p0[2], p1[0], p1[1], p1[2]);
            if (trimmed == null) return;

            IBody2 wireBody = (IBody2)trimmed.CreateWireBody();
            if (wireBody == null) return;

            wireBody.Display3(activeDoc, color, (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);
            combWireBodies.Add(wireBody);
        }

        // Packed as R | (G<<8) | (B<<16), same convention already used
        // by DrawMarkerArm (magenta) and DrawFaceNormal (red).
        private static int GetCombColor(int bucket)
        {
            switch (bucket)
            {
                case 2: return 0 | (0 << 8) | (230 << 16);   // worst sample -- blue
                case 1: return 230 | (0 << 8) | (0 << 16);   // out of tolerance -- red
                default: return 0 | (200 << 8) | (0 << 16);  // in tolerance -- green
            }
        }

        #endregion

        private static ulong CombineFingerprints(ulong a, ulong b)
        {
            unchecked
            {
                a ^= b;
                a *= 1099511628211UL;
                return a;
            }
        }

        // Cheap signature for "what isolation + tolerance state is this
        // body being processed under right now" -- used only to gate
        // the pre-tessellation shortcut in lastKnownBodyState above.
        // Does NOT need to be as collision-resistant as the real content
        // fingerprint below, since a false match here only ever leads to
        // a bodyMeshCache lookup that would itself need to also match to
        // actually skip tessellation -- two independent hashes would
        // need to collide together for any incorrect result.
        private static ulong ComputeStateSignature(Face2[] faceFilter)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;

                void MixULong(ulong v) { hash ^= v; hash *= prime; }
                void MixDouble(double d)
                {
                    ulong bits = (ulong)BitConverter.DoubleToInt64Bits(Math.Round(d, 6));
                    MixULong(bits);
                }

                MixDouble(MeshToleranceMM);
                MixDouble(ChordAngleDeg);

                if (faceFilter == null)
                {
                    MixULong(0UL); // sentinel: whole body, not isolated
                }
                else
                {
                    var ids = new List<long>();
                    foreach (var f in faceFilter) ids.Add(GetComIdentity(f).ToInt64());
                    ids.Sort();
                    MixULong((ulong)ids.Count);
                    foreach (var id in ids) MixULong(unchecked((ulong)id));
                }

                return hash;
            }
        }

        private static ulong ComputeSkeletonFingerprint(List<(Face2 face, double[][] pts, double[][] normals, int[] vertIds)> tris)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;

                void Mix(double d)
                {
                    double rounded = Math.Round(d, 6);
                    ulong bits = (ulong)BitConverter.DoubleToInt64Bits(rounded);
                    hash ^= bits;
                    hash *= prime;
                }

                Mix(tris.Count);
                Mix(MeshToleranceMM);
                Mix(ChordAngleDeg);

                foreach (var tri in tris)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Mix(tri.pts[i][0]);
                        Mix(tri.pts[i][1]);
                        Mix(tri.pts[i][2]);
                    }
                }

                return hash;
            }
        }

        private float[] fixedViewDirModel;

        private void SetDefaultReferenceDirection()
        {
            fixedViewDirModel = new float[] { 0f, 1f, 0f };
        }

        private void SetPlaneFromCurrentView()
        {
            try
            {
                if (activeView == null) return;

                var xform = (MathTransform)activeView.Transform;
                double[] sw = (double[])xform.ArrayData;

                double dx = sw[6], dy = sw[7], dz = sw[8];
                double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (len < 0.0001) return;

                fixedViewDirModel = new[] { (float)(dx / len), (float)(dy / len), (float)(dz / len) };
                OverlayMode = 0;
                RequestOverlayRefresh();
            }
            catch (Exception ex)
            {
                Debug.Print("SetPlaneFromCurrentView failed: " + ex);
            }
        }

        private void SetPlaneToAxis(float x, float y, float z)
        {
            fixedViewDirModel = new[] { x, y, z };
            OverlayMode = 0;
            RequestOverlayRefresh();
        }

        public static double NormalLengthMM = 5.0;
        public static bool NormalsShown = false;

        // Per-document normal-vector temp graphics, keyed by document
        // COM identity. SolidWorks already keeps a document's own temp
        // graphics correctly isolated to that document's view -- the
        // earlier single shared list/flag fought against that by
        // forcibly clearing state on doc switches, which either
        // destroyed a DIFFERENT doc's normals or left stale ones stuck,
        // depending on timing. Per-doc storage lets each document's
        // Show/Clear act purely on its own entry, with SW handling the
        // cross-document isolation exactly as it already does natively.
        private readonly Dictionary<IntPtr, List<IBody2>> normalWireBodiesByDoc = new Dictionary<IntPtr, List<IBody2>>();

        private void ToggleNormals()
        {
            if (NormalsShown)
            {
                ClearNormals();
                NormalsShown = false;
            }
            else
            {
                ShowNormals();
                NormalsShown = true;
            }
            settingsPanel?.SyncNormalsButtonState();
        }

        private void ShowNormals()
        {
            try
            {
                if (activeDoc == null || !(activeDoc is PartDoc partDoc)) return;

                IntPtr docId = GetComIdentity(activeDoc);
                ClearNormalsForDoc(docId); // clear only this doc's own previous entry, if any

                var bodiesForThisDoc = new List<IBody2>();
                normalWireBodiesByDoc[docId] = bodiesForThisDoc;

                double modelLength = NormalLengthMM / 1000.0;

                var modeler = (Modeler)iSwApp.GetModeler();
                var allBodies = new List<object>();
                if (partDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) is object[] solidArr)
                    allBodies.AddRange(solidArr);
                if (partDoc.GetBodies2((int)swBodyType_e.swSheetBody, true) is object[] sheetArr)
                    allBodies.AddRange(sheetArr);

                foreach (object b in allBodies)
                {
                    var body = (IBody2)b;
                    if (!body.Visible) continue;

                    object[] faces = (object[])body.GetFaces();
                    if (faces == null) continue;

                    foreach (object f in faces)
                    {
                        DrawFaceNormal((Face2)f, modeler, modelLength, bodiesForThisDoc);
                    }
                }

                RequestOverlayRefresh();
            }
            catch (Exception ex)
            {
                Debug.Print("ShowNormals failed: " + ex);
            }
        }

        private void DrawFaceNormal(Face2 face, Modeler modeler, double normalLength, List<IBody2> targetList)
        {
            double[] uvBounds = (double[])face.GetUVBounds();
            double centerU = (uvBounds[0] + uvBounds[1]) / 2.0;
            double centerV = (uvBounds[2] + uvBounds[3]) / 2.0;

            var surf = (Surface)face.GetSurface();
            double[] eval = (double[])surf.Evaluate(centerU, centerV, 0, 0);

            double px = eval[0], py = eval[1], pz = eval[2];
            double nx = eval[3], ny = eval[4], nz = eval[5];

            if (face.FaceInSurfaceSense())
            {
                nx = -nx; ny = -ny; nz = -nz;
            }

            double ex = px + nx * normalLength;
            double ey = py + ny * normalLength;
            double ez = pz + nz * normalLength;

            ICurve line = (ICurve)modeler.CreateLine(new[] { px, py, pz }, new[] { nx, ny, nz });
            if (line == null) return;

            ICurve trimmed = (ICurve)line.CreateTrimmedCurve2(px, py, pz, ex, ey, ez);
            if (trimmed == null) return;

            IBody2 wireBody = (IBody2)trimmed.CreateWireBody();
            if (wireBody == null) return;

            int color = 255;
            wireBody.Display3(activeDoc, color, (int)swTempBodySelectOptions_e.swTempBodySelectOptionNone);

            targetList.Add(wireBody);
        }

        // Releases and forgets one document's own normal wire bodies,
        // keyed by COM identity -- no redraw, no effect on any other
        // document's entry. Safe to call for a doc with no entry (no-op).
        private void ClearNormalsForDoc(IntPtr docId)
        {
            if (!normalWireBodiesByDoc.TryGetValue(docId, out var bodies)) return;

            foreach (var body in bodies)
            {
                try
                {
                    Marshal.ReleaseComObject(body);
                }
                catch (Exception ex)
                {
                    Debug.Print("ReleaseComObject on normal wire body failed: " + ex);
                }
            }
            normalWireBodiesByDoc.Remove(docId);
        }

        // Clears normals for the CURRENTLY ACTIVE document only. This is
        // always correct to redraw immediately, since it only ever acts
        // on whichever document is genuinely active/visible right now.
        private void ClearNormals()
        {
            if (activeDoc == null) return;
            ClearNormalsForDoc(GetComIdentity(activeDoc));
            RequestOverlayRefresh();
        }

        private void AppendBodyTessellation(IBody2 body, List<(Face2 face, double[][] pts, double[][] normals, int[] vertIds)> outTriangles, Face2[] faceFilter = null)
        {
            // faceFilter, when provided, is passed straight through to
            // SW's own GetTessellation -- confirmed via official SW docs
            // (consistent 2017 through 2026): its FaceList parameter
            // scopes tessellation itself to just those faces ("if this
            // is empty, then SOLIDWORKS will tessellate the body").
            // Previously always passed null here, meaning isolating a
            // couple of faces on a large model still paid the full cost
            // of tessellating every face in the body, then threw away
            // most of the result via the RemoveAll filter downstream.
            // Same ITessellation object either way, so
            // CurveChordTolerance/CurveChordAngleTolerance still apply
            // identically -- no precision tradeoff versus the older,
            // simpler per-face tessellation API.
            ITessellation tess = (ITessellation)body.GetTessellation(faceFilter);
            if (tess == null) return;

            tess.NeedVertexNormal = true;
            tess.NeedFaceFacetMap = true;

            // Confirmed real ITessellation members (independently verified
            // this session via a PowerShell reflection query against the
            // actual referenced interop DLL, not just doc claims). SW's
            // display tessellation (whatever the part's own Image Quality
            // setting produces) was the root cause of the sliver artifact
            // [stated] found: our old subdivision code only operated
            // INSIDE whatever skeleton triangle SW handed us, so a coarse
            // chord near a curved edge stayed coarse no matter how much
            // we subdivided it. Setting these directly makes the skeleton
            // tessellation itself precise and independent of the part's
            // own display/image-quality setting, driven by [stated]'s
            // own panel controls (mm/degrees) rather than a hidden
            // subdivision multiplier.
            // Units: SW's API is meters internally; angles in radians.
            double chordToleranceM = MeshToleranceMM / 1000.0;
            double chordAngleRad = ChordAngleDeg * Math.PI / 180.0;
            tess.CurveChordTolerance = chordToleranceM;
            tess.CurveChordAngleTolerance = chordAngleRad;
            tess.SurfacePlaneTolerance = chordToleranceM;
            tess.SurfacePlaneAngleTolerance = chordAngleRad;
            tess.ImprovedQuality = true;

            tess.Tessellate();

            object faceObj = body.GetFirstFace();
            while (faceObj != null)
            {
                var face = (Face2)faceObj;
                int[] facetIds = (int[])tess.GetFaceFacets(face);

                if (facetIds != null)
                {
                    foreach (int facetId in facetIds)
                    {
                        int[] finIds = (int[])tess.GetFacetFins(facetId);

                        var cornerPts = new double[3][];
                        var cornerNormals = new double[3][];
                        var vertIds = new int[3];
                        for (int i = 0; i < 3; i++)
                        {
                            int[] finVertIds = (int[])tess.GetFinVertices(finIds[i]);
                            vertIds[i] = finVertIds[0];
                            cornerPts[i] = (double[])tess.GetVertexPoint(finVertIds[0]);
                            // SW's own tessellation vertex normal -- confirmed
                            // exact (0.0000 deg max/avg angular difference vs
                            // our own Surface.Evaluate re-derivation, 500
                            // samples) this session, so used directly rather
                            // than re-deriving via GetClosestPointOn+Evaluate.
                            cornerNormals[i] = (double[])tess.GetVertexNormal(finVertIds[0]);
                        }

                        outTriangles.Add((face, cornerPts, cornerNormals, vertIds));
                    }
                }

                faceObj = face.GetNextFace();
            }
        }

        // Replaces the old SubdivisionLevel: now that SW's own
        // tessellation vertex normal is confirmed exact (0.0000 deg
        // angular difference vs our own Surface.Evaluate re-derivation,
        // across 500 real samples this session), and mesh density/
        // boundary precision is fully governed by CurveChordTolerance/
        // CurveChordAngleTolerance directly, there's no remaining role
        // for a separate subdivision multiplier -- these two are now
        // the only mesh-density controls, exposed in the panel in
        // practical units (mm/degrees) rather than SW's internal
        // meters/radians.
        public static double MeshToleranceMM = 0.1;
        public static double ChordAngleDeg = 10.0;

        // Replaces AppendSubdividedTriangle/EmitSubTriangle/EvalGridVertex
        // (and, with them, GetFacePeriodicity/uvCache/periodicityCache --
        // all now unused). With mesh density fully governed by
        // CurveChordTolerance/CurveChordAngleTolerance in
        // AppendBodyTessellation, and SW's own tessellation vertex
        // normal confirmed exact, there's nothing left to subdivide --
        // each skeleton triangle from SW is emitted directly, using its
        // own already-exact position and normal. This also fully
        // retires the periodic-surface UV-unwrap logic: that was only
        // ever needed to fix artifacts from OUR OWN UV-space
        // interpolation crossing a periodic seam -- SW's native
        // triangles never had that problem in the first place, since
        // they're built from real 3D positions, not interpolated UV.
        private void AppendTriangle(Face2 face, double[][] cornerPts, double[][] cornerNormals, List<float> outBuf)
        {
            // NOTE: no FaceInSurfaceSense flip here, unlike the old
            // surf.Evaluate-based path. That flip corrected the raw
            // SURFACE normal (whose sign is independent of which face is
            // using it) into the face's true outward direction. SW's own
            // tess.GetVertexNormal() is a DISPLAY tessellation normal --
            // the one SW's native renderer uses directly to shade the
            // model -- so it should already be correctly face-oriented
            // by construction. Applying the same flip on top produced a
            // visible seam: correct on faces where FaceInSurfaceSense()
            // is false, double-flipped (wrong) on faces where it's true
            // -- exactly the single hard discontinuity [stated] found
            // right at a face boundary, not a general breakage.
            for (int i = 0; i < 3; i++)
            {
                outBuf.Add((float)cornerPts[i][0]);
                outBuf.Add((float)cornerPts[i][1]);
                outBuf.Add((float)cornerPts[i][2]);
                outBuf.Add((float)cornerNormals[i][0]);
                outBuf.Add((float)cornerNormals[i][1]);
                outBuf.Add((float)cornerNormals[i][2]);
            }
        }


        #endregion

        #region Draw (legacy client-state VBO + compatibility-profile shader)

        public static float LineFrequency = 6f;
        public static float LineWidth = 0.08f;
        public static float LineFeather = 0.5f;
        public static int OverlayMode = 1;

        public static int LastVisiblePolygonCount = 0;

        public static int MatcapAxis = 1;

        public static bool OverlayEnabled = false;
        public static bool StripesOnly = false;

        // Open issue #9: the overlay paints over SW's heads-up view
        // toolbar because GraphicsRenderPostNotify fires after SW
        // composites the toolbar onto the same render target. The
        // toolbar should stay visible whenever the user has it visible
        // -- the fix is to keep our own draws out of its way, not to
        // hide it. No API was found to query the toolbar's exact rect
        // (HWND enumeration, swUserPreferenceToggle_e, macro-recorded
        // toggle, and IFrame were all checked and ruled out), so this is
        // a heuristic top-of-viewport exclusion band via glScissor, not
        // a precise fit to the toolbar's actual bounds. Tunable because
        // the real toolbar width/position varies with customization and
        // DPI. (IModelView::VisibilityViewTools exists and does toggle
        // the toolbar's visibility, but was the wrong fix -- it hides
        // the toolbar rather than just clipping the overlay away from
        // it, which is not what's wanted here.)
        public static float TopBandHeightPx = 30f;

        // Checks glGetError() and, if non-zero, prints it tagged with a
        // label identifying where in the draw sequence it was checked.
        // Calling glGetError() resets the error flag as a side effect,
        // so placing several of these through DrawMesh lets the first
        // one to report non-zero pinpoint which segment introduced the
        // error, rather than only knowing it happened somewhere in the
        // whole method.
        private static void CheckGLError(string label)
        {
            uint err = GL.GetError();
            if (err != 0) Debug.Print("glGetError [" + label + "]: 0x" + err.ToString("X4"));
        }

        private bool IsCurrentlyShadedWithEdges()
        {
            if (activeView == null) return false;
            try
            {
                return activeView.DisplayMode == (int)swViewDisplayMode_e.swViewDisplayMode_ShadedWithEdges;
            }
            catch
            {
                return false;
            }
        }

        // Saved so ApplyTopBandScissor/RestoreScissor can put SW's own
        // scissor state back exactly as found -- SW's renderer may rely
        // on scissor state itself elsewhere in the frame, so this must
        // not just blindly disable it afterward.
        private bool savedScissorEnabled;
        private readonly int[] savedScissorBox = new int[4];

        // Clips a fixed-height band off the top of the current viewport
        // before any overlay drawing -- applies uniformly to the shader
        // mesh draw, the fixed-function model-edge line draw, and the
        // isocurve draw, since it's a single GL clip rect rather than
        // per-shader discard logic. See TopBandHeightPx for why this is
        // a heuristic band rather than the toolbar's real bounds.
        private void ApplyTopBandScissor()
        {
            savedScissorEnabled = GL.IsEnabled(GL.GL_SCISSOR_TEST);
            GL.GetIntegerv(GL.GL_SCISSOR_BOX, savedScissorBox);

            var viewportBox = new int[4];
            GL.GetIntegerv(GL.GL_VIEWPORT, viewportBox);
            int vpX = viewportBox[0], vpY = viewportBox[1], vpW = viewportBox[2], vpH = viewportBox[3];

            int bandPx = (int)Math.Max(0f, TopBandHeightPx);
            int clippedHeight = Math.Max(0, vpH - bandPx);

            GL.Enable(GL.GL_SCISSOR_TEST);
            GL.Scissor(vpX, vpY, vpW, clippedHeight);
        }

        private void RestoreScissor()
        {
            if (savedScissorEnabled)
            {
                GL.Scissor(savedScissorBox[0], savedScissorBox[1], savedScissorBox[2], savedScissorBox[3]);
            }
            else
            {
                GL.Disable(GL.GL_SCISSOR_TEST);
            }
        }

        // Used by OnBufferSwapNotify's GL-context-recovery path. Each
        // dispose is wrapped separately: if the handles are genuinely
        // dangling (the context-recreation hypothesis), the underlying
        // glDeleteProgram/glDeleteBuffer calls are well-defined no-ops
        // on invalid names per the GL spec (they just set another error
        // flag, not a crash) -- but wrapping defensively anyway since
        // this runs in exactly the situation we're least sure about the
        // GL state. Nulling all four means the existing lazy-recreation
        // logic (shaderProgram == null check, meshCacheDirty flag)
        // naturally rebuilds everything fresh next frame.
        private void DisposeAllCachedGLResources()
        {
            try { shaderProgram?.Dispose(); } catch (Exception ex) { Debug.Print("DisposeAllCachedGLResources: shaderProgram.Dispose() failed: " + ex); }
            try { meshBuffer?.Dispose(); } catch (Exception ex) { Debug.Print("DisposeAllCachedGLResources: meshBuffer.Dispose() failed: " + ex); }
            try { lineBuffer?.Dispose(); } catch (Exception ex) { Debug.Print("DisposeAllCachedGLResources: lineBuffer.Dispose() failed: " + ex); }
            try { isocurveBuffer?.Dispose(); } catch (Exception ex) { Debug.Print("DisposeAllCachedGLResources: isocurveBuffer.Dispose() failed: " + ex); }

            shaderProgram = null;
            meshBuffer = null;
            lineBuffer = null;
            isocurveBuffer = null;
        }

        private void DrawMesh()
        {
            if (!OverlayEnabled) return;
            if (meshBuffer == null) return;

            // Bisecting the recurring 0x0502 (GL_INVALID_OPERATION):
            // glGetError() resets the error flag as a side effect of
            // being queried, so checking at several points through this
            // method means the FIRST checkpoint to report non-zero
            // pinpoints which segment introduced it, rather than just
            // knowing it happened somewhere in the whole draw call.
            CheckGLError("DrawMesh entry (baseline, before touching GL state)");

            if (shaderProgram == null)
            {
                shaderProgram = new ShaderProgram(VertexShaderSrc, FragmentShaderSrc);
            }

            bool prevDepthTest = GL.IsEnabled(GL.GL_DEPTH_TEST);
            bool prevCullFace = GL.IsEnabled(GL.GL_CULL_FACE);
            bool prevLighting = GL.IsEnabled(GL.GL_LIGHTING);
            bool prevBlend = GL.IsEnabled(GL.GL_BLEND);
            bool prevPolygonOffsetFill = GL.IsEnabled(GL.GL_POLYGON_OFFSET_FILL);
            GL.GetIntegerv(GL.GL_CURRENT_PROGRAM, out int prevProgram);
            GL.GetIntegerv(GL.GL_DEPTH_FUNC, out int prevDepthFunc);

            GL.Enable(GL.GL_DEPTH_TEST);
            GL.Disable(GL.GL_CULL_FACE);
            GL.Disable(GL.GL_LIGHTING);

            GL.Enable(GL.GL_BLEND);
            GL.BlendFunc(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA);

            GL.DepthFunc(GL.GL_LEQUAL);
            GL.Enable(GL.GL_POLYGON_OFFSET_FILL);
            GL.PolygonOffset(-2f, -10f);

            shaderProgram.Use();

            if (fixedViewDirModel != null)
            {
                shaderProgram.SetVec3("uRefDir", fixedViewDirModel[0], fixedViewDirModel[1], fixedViewDirModel[2]);
            }
            shaderProgram.SetFloat("uLineFrequency", LineFrequency);
            shaderProgram.SetFloat("uLineWidth", LineWidth);
            shaderProgram.SetFloat("uLineFeather", LineFeather);
            shaderProgram.SetFloat("uMode", (float)OverlayMode);
            float axX = MatcapAxis == 0 ? 1f : 0f;
            float axY = MatcapAxis == 1 ? 1f : 0f;
            shaderProgram.SetVec3("uMatcapAxis", axX, axY, 0f);
            shaderProgram.SetFloat("uStripesOnly", StripesOnly ? 1f : 0f);

            CheckGLError("DrawMesh after shader Use + uniform sets, before meshBuffer.Draw()");

            meshBuffer.Draw();

            CheckGLError("DrawMesh after meshBuffer.Draw()");

            if (!StripesOnly && lineBuffer != null && IsCurrentlyShadedWithEdges())
            {
                GL.UseProgram(0);

                double[] prevDepthRange = new double[2];
                GL.GetDoublev(GL.GL_DEPTH_RANGE, prevDepthRange);
                GL.DepthRange(0.0, 0.9999);

                GL.Color3f(0.0f, 0.0f, 0.0f);
                lineBuffer.Draw();

                GL.DepthRange(prevDepthRange[0], prevDepthRange[1]);

                CheckGLError("DrawMesh after lineBuffer.Draw() (edge-line block)");
            }

            // prevProgram was captured before our draw calls, from
            // whatever program SW itself had bound. If SW deletes or
            // recreates that program object in between -- plausible
            // during an active model rebuild, which is exactly the
            // window this crash keeps occurring in -- restoring a now-
            // invalid handle is a GL_INVALID_OPERATION per spec, and
            // critically does NOT actually change the bound program (it
            // stays whatever we last set), leaving SW's own renderer to
            // find a different program bound than it expects. Guard
            // against that: only restore if it's still a genuinely
            // valid program, binding 0 (fixed-function, always valid)
            // otherwise.
            if (prevProgram == 0 || GL.IsProgram((uint)prevProgram))
            {
                GL.UseProgram((uint)prevProgram);
            }
            else
            {
                Debug.Print("DrawMesh: prevProgram " + prevProgram + " is no longer a valid program, binding 0 instead.");
                GL.UseProgram(0);
            }
            uint errAfterProgramRestore = GL.GetError();
            if (errAfterProgramRestore != 0) Debug.Print("glGetError immediately after DrawMesh program restore: 0x" + errAfterProgramRestore.ToString("X4"));

            GL.DepthFunc((uint)prevDepthFunc);
            if (!prevPolygonOffsetFill) GL.Disable(GL.GL_POLYGON_OFFSET_FILL);
            if (!prevBlend) GL.Disable(GL.GL_BLEND);
            if (!prevDepthTest) GL.Disable(GL.GL_DEPTH_TEST);
            if (prevCullFace) GL.Enable(GL.GL_CULL_FACE);
            if (prevLighting) GL.Enable(GL.GL_LIGHTING);

            uint err = GL.GetError();
            if (err != 0) Debug.Print("glGetError after mesh draw: 0x" + err.ToString("X4"));
        }

        private void DrawIsocurves()
        {
            if (!IsIsocurveIsolating) return;
            if (isocurveBuffer == null) return;

            CheckGLError("DrawIsocurves entry (baseline, before touching GL state)");

            bool prevDepthTest = GL.IsEnabled(GL.GL_DEPTH_TEST);
            bool prevLighting = GL.IsEnabled(GL.GL_LIGHTING);
            GL.GetIntegerv(GL.GL_CURRENT_PROGRAM, out int prevProgram);
            GL.GetIntegerv(GL.GL_DEPTH_FUNC, out int prevDepthFunc);

            GL.Enable(GL.GL_DEPTH_TEST);
            GL.Disable(GL.GL_LIGHTING);
            GL.DepthFunc(GL.GL_LEQUAL);
            GL.UseProgram(0);

            double[] prevDepthRange = new double[2];
            GL.GetDoublev(GL.GL_DEPTH_RANGE, prevDepthRange);
            GL.DepthRange(0.0, 0.9999);

            double[] prevLineWidth = new double[1];
            GL.GetDoublev(0x0B21, prevLineWidth); // GL_LINE_WIDTH
            GL.LineWidth(2.0f);

            GL.Color3f(0.0f, 0.7f, 0.0f); // green
            isocurveBuffer.DrawRange(0, isocurveAlongUVertexCount);

            GL.Color3f(0.9f, 0.0f, 0.0f); // red
            isocurveBuffer.DrawRange(isocurveAlongUVertexCount, isocurveBuffer.VertexCount - isocurveAlongUVertexCount);

            CheckGLError("DrawIsocurves after isocurveBuffer.Draw()");

            GL.LineWidth((float)prevLineWidth[0]);

            GL.DepthRange(prevDepthRange[0], prevDepthRange[1]);
            if (prevProgram == 0 || GL.IsProgram((uint)prevProgram))
            {
                GL.UseProgram((uint)prevProgram);
            }
            else
            {
                Debug.Print("DrawIsocurves: prevProgram " + prevProgram + " is no longer a valid program, binding 0 instead.");
                GL.UseProgram(0);
            }
            GL.DepthFunc((uint)prevDepthFunc);
            if (!prevDepthTest) GL.Disable(GL.GL_DEPTH_TEST);
            if (prevLighting) GL.Enable(GL.GL_LIGHTING);

            CheckGLError("DrawIsocurves end (was previously unchecked here -- possible source of errors carrying into the next frame)");
        }

        private const string VertexShaderSrc = @"
#version 120
varying vec3 vNormal;

void main()
{
    gl_Position = gl_ModelViewProjectionMatrix * gl_Vertex;
    vNormal = gl_Normal;
}";

        private const string FragmentShaderSrc = @"
#version 120
varying vec3 vNormal;

uniform vec3 uRefDir;
uniform float uLineFrequency;
uniform float uLineWidth;
uniform float uLineFeather;
uniform float uMode;
uniform vec3 uMatcapAxis;
uniform float uStripesOnly;

void main()
{
    vec3 N_model = normalize(vNormal);
    float ndotv;
    float rawBand;

    if (uMode > 0.5)
    {
        vec3 N_view = normalize(gl_NormalMatrix * N_model);
        ndotv = clamp(dot(N_view, uMatcapAxis), -1.0, 1.0);
        float angleDeg = degrees(acos(ndotv));
        rawBand = angleDeg * uLineFrequency / 90.0;
    }
    else
    {
        ndotv = clamp(dot(N_model, uRefDir), -1.0, 1.0);
        float angleDeg = degrees(acos(ndotv));
        rawBand = angleDeg * uLineFrequency / 90.0;
    }

    float band = fract(rawBand);

    float aa = fwidth(rawBand);
    float feather = max(uLineFeather * aa, 0.0001);

    float lowEdge = smoothstep(uLineWidth - feather, uLineWidth + feather, band);
    float highEdge = smoothstep(1.0 - uLineWidth - feather, 1.0 - uLineWidth + feather, band);
    float isLine = (1.0 - lowEdge) + highEdge;
    isLine = clamp(isLine, 0.0, 1.0);

    vec3 baseColor = mix(vec3(0.15, 0.35, 0.9), vec3(0.9, 0.25, 0.15), (ndotv + 1.0) * 0.5);
    vec3 lineColor = vec3(0.05, 0.05, 0.05);

    vec3 finalColor = mix(mix(baseColor, lineColor, isLine), lineColor, uStripesOnly);

    float alpha = mix(1.0, isLine, uStripesOnly);

    if (alpha < 0.01) discard;

    gl_FragColor = vec4(finalColor, alpha);
}";

        #endregion

        #region COM registration

        [ComRegisterFunction]
        private static void RegisterFunction(Type t)
        {
            var keyPath = @"SOFTWARE\SolidWorks\Addins\{" + t.GUID.ToString() + "}";
            using (var key = Registry.LocalMachine.CreateSubKey(keyPath))
            {
                key.SetValue(null, 0);
                key.SetValue("Description", "Additional Surface Analysis Tools With User Controllable Mesh Settings");
                key.SetValue("Title", "Surface Analysis Extension");
            }

            var startupPath = @"Software\SolidWorks\AddInsStartup\{" + t.GUID.ToString() + "}";
            using (var key = Registry.CurrentUser.CreateSubKey(startupPath))
            {
                key.SetValue(null, 1);
            }
        }

        [ComUnregisterFunction]
        private static void UnregisterFunction(Type t)
        {
            Registry.LocalMachine.DeleteSubKey(
                @"SOFTWARE\SolidWorks\Addins\{" + t.GUID.ToString() + "}", false);
            Registry.CurrentUser.DeleteSubKey(
                @"Software\SolidWorks\AddInsStartup\{" + t.GUID.ToString() + "}", false);
        }

        #endregion
    }

    // Read-only callout handler -- ISwCalloutHandler has exactly ONE
    // required member (confirmed via the interop assembly and the
    // official Members page). CreateCallout requires a non-null
    // handler, but since the G2 callout never exposes an editable
    // value (ValueInactive is set on every row), this is a pure no-op.
    [ComVisible(true)]
    public class G2CalloutHandler : ISwCalloutHandler
    {
        public bool OnStringValueChanged(object pManipulator, int Index, string Text)
        {
            return false;
        }
    }

    // Read-only callout handler for the per-face isocurve degree/CV
    // callouts -- same reasoning as G2CalloutHandler: CreateCallout
    // requires a non-null handler, and this callout never exposes an
    // editable value (ValueInactive is set on every row), so it's a
    // pure no-op. Kept as its own class rather than reusing
    // G2CalloutHandler so the two callout features stay independent.
    [ComVisible(true)]
    public class IsocurveCalloutHandler : ISwCalloutHandler
    {
        public bool OnStringValueChanged(object pManipulator, int Index, string Text)
        {
            return false;
        }
    }

    // Polls raw OS key state directly, bypassing the Windows message
    // queue -- needed because RebuildMeshBuffer runs synchronously on
    // SW's main thread, so no message pump is running to deliver a
    // normal keypress event while it's mid-loop.
    internal static class EscKeyWatcher
    {
        private const int VK_ESCAPE = 0x1B;

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public static bool IsEscapePressed()
        {
            short state = GetAsyncKeyState(VK_ESCAPE);
            // High bit = currently held down; low bit = was pressed at
            // some point since the last call. Checking both catches a
            // quick tap that lands between two polling intervals, which
            // the high bit alone can miss if the key's already back up
            // by the time the next check runs.
            return (state & 0x8000) != 0 || (state & 0x0001) != 0;
        }
    }

    internal static class GL
    {
        private const string Lib = "opengl32.dll";

        public const uint GL_LINES = 0x0001;
        public const uint GL_TRIANGLES = 0x0004;
        public const uint GL_LIGHTING = 0x0B50;
        public const uint GL_DEPTH_TEST = 0x0B71;
        public const uint GL_DEPTH_FUNC = 0x0B74;
        public const uint GL_DEPTH_RANGE = 0x0B70;
        public const uint GL_LEQUAL = 0x0203;
        public const uint GL_CULL_FACE = 0x0B44;
        public const uint GL_POLYGON_OFFSET_FILL = 0x8037;
        public const uint GL_POLYGON_OFFSET_LINE = 0x2A02;
        public const uint GL_BLEND = 0x0BE2;
        public const uint GL_SRC_ALPHA = 0x0302;
        public const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303;
        public const uint GL_CURRENT_PROGRAM = 0x8B8D;
        public const uint GL_SCISSOR_TEST = 0x0C11;
        public const uint GL_SCISSOR_BOX = 0x0C10;
        public const uint GL_VIEWPORT = 0x0BA2;
        public const uint GL_ARRAY_BUFFER = 0x8892;
        public const uint GL_STATIC_DRAW = 0x88E4;
        public const uint GL_FLOAT = 0x1406;
        public const uint GL_VERTEX_ARRAY = 0x8074;
        public const uint GL_NORMAL_ARRAY = 0x8075;
        public const uint GL_VERTEX_SHADER = 0x8B31;
        public const uint GL_FRAGMENT_SHADER = 0x8B30;
        public const uint GL_COMPILE_STATUS = 0x8B81;
        public const uint GL_LINK_STATUS = 0x8B82;

        #region GL 1.1 -- direct DllImport

        // Direct exports from opengl32.dll (not resolved via
        // wglGetProcAddress, unlike everything in the GL 1.5+/2.0+
        // region below) -- these work regardless of whether a context
        // is current, and are exactly how to check whether one IS
        // current. See open issue #10: wglGetProcAddress failing to
        // resolve otherwise-definitely-supported functions (glIsProgram,
        // glIsBuffer, glDeleteProgram) right at the crash-adjacent
        // moment is the classic signature of no context being current
        // on the calling thread -- this checks that directly instead of
        // inferring it from wglGetProcAddress's behavior.
        [DllImport(Lib, EntryPoint = "wglGetCurrentContext")]
        public static extern IntPtr GetCurrentContext();

        [DllImport(Lib, EntryPoint = "wglGetCurrentDC")]
        public static extern IntPtr GetCurrentDC();

        [DllImport(Lib, EntryPoint = "glIsEnabled")]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool IsEnabled(uint cap);

        [DllImport(Lib, EntryPoint = "glDisable")]
        public static extern void Disable(uint cap);

        [DllImport(Lib, EntryPoint = "glEnable")]
        public static extern void Enable(uint cap);

        [DllImport(Lib, EntryPoint = "glPolygonOffset")]
        public static extern void PolygonOffset(float factor, float units);

        [DllImport(Lib, EntryPoint = "glColor3f")]
        public static extern void Color3f(float r, float g, float b);

        [DllImport(Lib, EntryPoint = "glLineWidth")]
        public static extern void LineWidth(float width);

        [DllImport(Lib, EntryPoint = "glDepthFunc")]
        public static extern void DepthFunc(uint func);

        [DllImport(Lib, EntryPoint = "glDepthRange")]
        public static extern void DepthRange(double near, double far);

        [DllImport(Lib, EntryPoint = "glGetDoublev")]
        public static extern void GetDoublev(uint pname, double[] data);

        [DllImport(Lib, EntryPoint = "glBlendFunc")]
        public static extern void BlendFunc(uint sfactor, uint dfactor);

        [DllImport(Lib, EntryPoint = "glGetError")]
        public static extern uint GetError();

        [DllImport(Lib, EntryPoint = "glGetIntegerv")]
        public static extern void GetIntegerv(uint pname, out int data);

        [DllImport(Lib, EntryPoint = "glGetIntegerv")]
        public static extern void GetIntegerv(uint pname, int[] data);

        [DllImport(Lib, EntryPoint = "glScissor")]
        public static extern void Scissor(int x, int y, int width, int height);

        [DllImport(Lib, EntryPoint = "glDrawArrays")]
        public static extern void DrawArrays(uint mode, int first, int count);

        [DllImport(Lib, EntryPoint = "glEnableClientState")]
        public static extern void EnableClientState(uint cap);

        [DllImport(Lib, EntryPoint = "glDisableClientState")]
        public static extern void DisableClientState(uint cap);

        [DllImport(Lib, EntryPoint = "glVertexPointer")]
        public static extern void VertexPointer(int size, uint type, int stride, IntPtr pointer);

        [DllImport(Lib, EntryPoint = "glNormalPointer")]
        public static extern void NormalPointer(uint type, int stride, IntPtr pointer);

        #endregion

        #region GL 1.5+/2.0+ -- resolved at runtime, cached per-name

        [DllImport(Lib, EntryPoint = "wglGetProcAddress")]
        private static extern IntPtr WglGetProcAddress(string name);

        private static readonly Dictionary<string, Delegate> procCache = new Dictionary<string, Delegate>();

        private static TDelegate GetProc<TDelegate>(string name) where TDelegate : class
        {
            if (procCache.TryGetValue(name, out Delegate cached))
                return cached as TDelegate;

            IntPtr ptr = WglGetProcAddress(name);
            if (ptr == IntPtr.Zero)
            {
                Debug.Print("wglGetProcAddress could not resolve " + name);
                procCache[name] = null;
                return null;
            }

            Delegate del = Marshal.GetDelegateForFunctionPointer(ptr, typeof(TDelegate));
            procCache[name] = del;
            return (TDelegate)(object)del;
        }

        private delegate void UseProgramDelegate(uint program);
        public static void UseProgram(uint program) => GetProc<UseProgramDelegate>("glUseProgram")?.Invoke(program);

        private delegate byte IsProgramDelegate(uint program);
        public static bool IsProgram(uint program)
        {
            var fn = GetProc<IsProgramDelegate>("glIsProgram");
            // Unresolvable is a DIFFERENT condition from "resolved and
            // says invalid" -- collapsing them via `?.Invoke() != 0`
            // silently returns true (valid!) when the function can't
            // even be found, which is backwards. Returning false here
            // treats "couldn't check" as "not confirmed valid", which
            // is the safer and more honest default.
            return fn != null && fn(program) != 0;
        }

        private delegate byte IsBufferDelegate(uint buffer);
        public static bool IsBuffer(uint buffer)
        {
            var fn = GetProc<IsBufferDelegate>("glIsBuffer");
            return fn != null && fn(buffer) != 0;
        }

        private delegate void GenBuffersDelegate(int n, uint[] buffers);
        public static uint GenBuffer()
        {
            uint[] ids = new uint[1];
            GetProc<GenBuffersDelegate>("glGenBuffers")?.Invoke(1, ids);
            return ids[0];
        }

        private delegate void BindBufferDelegate(uint target, uint buffer);
        public static void BindBuffer(uint target, uint buffer) => GetProc<BindBufferDelegate>("glBindBuffer")?.Invoke(target, buffer);

        private delegate void DeleteBuffersDelegate(int n, uint[] buffers);
        public static void DeleteBuffer(uint buffer) => GetProc<DeleteBuffersDelegate>("glDeleteBuffers")?.Invoke(1, new[] { buffer });

        private delegate void BufferDataDelegate(uint target, IntPtr size, float[] data, uint usage);
        public static void BufferData(uint target, float[] data, uint usage) =>
            GetProc<BufferDataDelegate>("glBufferData")?.Invoke(target, (IntPtr)(data.Length * sizeof(float)), data, usage);

        private delegate uint CreateShaderDelegate(uint type);
        public static uint CreateShader(uint type) => GetProc<CreateShaderDelegate>("glCreateShader")?.Invoke(type) ?? 0;

        private delegate void ShaderSourceDelegate(uint shader, int count, IntPtr[] strings, int[] lengths);
        public static void ShaderSource(uint shader, string source)
        {
            IntPtr strPtr = Marshal.StringToHGlobalAnsi(source);
            try
            {
                GetProc<ShaderSourceDelegate>("glShaderSource")?.Invoke(shader, 1, new[] { strPtr }, null);
            }
            finally
            {
                Marshal.FreeHGlobal(strPtr);
            }
        }

        private delegate void CompileShaderDelegate(uint shader);
        public static void CompileShader(uint shader) => GetProc<CompileShaderDelegate>("glCompileShader")?.Invoke(shader);

        private delegate void GetShaderivDelegate(uint shader, uint pname, out int param);
        public static int GetShaderiv(uint shader, uint pname)
        {
            int val = 0;
            GetProc<GetShaderivDelegate>("glGetShaderiv")?.Invoke(shader, pname, out val);
            return val;
        }

        private delegate void GetShaderInfoLogDelegate(uint shader, int maxLength, out int length, StringBuilder infoLog);
        public static string GetShaderInfoLog(uint shader)
        {
            var sb = new StringBuilder(2048);
            GetProc<GetShaderInfoLogDelegate>("glGetShaderInfoLog")?.Invoke(shader, sb.Capacity, out int len, sb);
            return sb.ToString();
        }

        private delegate void DeleteShaderDelegate(uint shader);
        public static void DeleteShader(uint shader) => GetProc<DeleteShaderDelegate>("glDeleteShader")?.Invoke(shader);

        private delegate uint CreateProgramDelegate();
        public static uint CreateProgram() => GetProc<CreateProgramDelegate>("glCreateProgram")?.Invoke() ?? 0;

        private delegate void AttachShaderDelegate(uint program, uint shader);
        public static void AttachShader(uint program, uint shader) => GetProc<AttachShaderDelegate>("glAttachShader")?.Invoke(program, shader);

        private delegate void LinkProgramDelegate(uint program);
        public static void LinkProgram(uint program) => GetProc<LinkProgramDelegate>("glLinkProgram")?.Invoke(program);

        private delegate void GetProgramivDelegate(uint program, uint pname, out int param);
        public static int GetProgramiv(uint program, uint pname)
        {
            int val = 0;
            GetProc<GetProgramivDelegate>("glGetProgramiv")?.Invoke(program, pname, out val);
            return val;
        }

        private delegate void GetProgramInfoLogDelegate(uint program, int maxLength, out int length, StringBuilder infoLog);
        public static string GetProgramInfoLog(uint program)
        {
            var sb = new StringBuilder(2048);
            GetProc<GetProgramInfoLogDelegate>("glGetProgramInfoLog")?.Invoke(program, sb.Capacity, out int len, sb);
            return sb.ToString();
        }

        private delegate void DeleteProgramDelegate(uint program);
        public static void DeleteProgram(uint program) => GetProc<DeleteProgramDelegate>("glDeleteProgram")?.Invoke(program);

        private delegate int GetUniformLocationDelegate(uint program, string name);
        public static int GetUniformLocation(uint program, string name) => GetProc<GetUniformLocationDelegate>("glGetUniformLocation")?.Invoke(program, name) ?? -1;

        private delegate void Uniform1fDelegate(int location, float value);
        public static void Uniform1f(int location, float value) =>
            GetProc<Uniform1fDelegate>("glUniform1f")?.Invoke(location, value);

        private delegate void Uniform3fDelegate(int location, float x, float y, float z);
        public static void Uniform3f(int location, float x, float y, float z) =>
            GetProc<Uniform3fDelegate>("glUniform3f")?.Invoke(location, x, y, z);

        #endregion
    }

    internal class MeshBuffer : IDisposable
    {
        private readonly uint vbo;
        private readonly int vertexCount;
        private const int Stride = 6 * sizeof(float);

        // Investigating open issue #10 (SW crash during Instant3D forced
        // rebuild): if SW recreates its underlying GL context/surface,
        // every handle we've cached from before that point becomes
        // dangling. glIsBuffer tells us whether the driver still
        // recognizes this VBO in whatever context is CURRENTLY bound.
        public bool IsValid => GL.IsBuffer(vbo);

        public MeshBuffer(float[] interleavedData)
        {
            vertexCount = interleavedData.Length / 6;

            vbo = GL.GenBuffer();
            GL.BindBuffer(GL.GL_ARRAY_BUFFER, vbo);
            GL.BufferData(GL.GL_ARRAY_BUFFER, interleavedData, GL.GL_STATIC_DRAW);
            GL.BindBuffer(GL.GL_ARRAY_BUFFER, 0);
        }

        public void Draw()
        {
            GL.BindBuffer(GL.GL_ARRAY_BUFFER, vbo);

            GL.VertexPointer(3, GL.GL_FLOAT, Stride, IntPtr.Zero);
            GL.EnableClientState(GL.GL_VERTEX_ARRAY);

            GL.NormalPointer(GL.GL_FLOAT, Stride, (IntPtr)(3 * sizeof(float)));
            GL.EnableClientState(GL.GL_NORMAL_ARRAY);

            GL.DrawArrays(GL.GL_TRIANGLES, 0, vertexCount);

            GL.DisableClientState(GL.GL_VERTEX_ARRAY);
            GL.DisableClientState(GL.GL_NORMAL_ARRAY);

            GL.BindBuffer(GL.GL_ARRAY_BUFFER, 0);
        }

        public void Dispose()
        {
            GL.DeleteBuffer(vbo);
        }
    }

    internal class LineBuffer : IDisposable
    {
        private readonly uint vbo;
        private readonly int vertexCount;
        private const int Stride = 3 * sizeof(float);

        public bool IsValid => GL.IsBuffer(vbo);

        public int VertexCount => vertexCount;

        public LineBuffer(float[] positionData)
        {
            vertexCount = positionData.Length / 3;

            vbo = GL.GenBuffer();
            GL.BindBuffer(GL.GL_ARRAY_BUFFER, vbo);
            GL.BufferData(GL.GL_ARRAY_BUFFER, positionData, GL.GL_STATIC_DRAW);
            GL.BindBuffer(GL.GL_ARRAY_BUFFER, 0);
        }

        public void Draw()
        {
            GL.BindBuffer(GL.GL_ARRAY_BUFFER, vbo);

            GL.VertexPointer(3, GL.GL_FLOAT, Stride, IntPtr.Zero);
            GL.EnableClientState(GL.GL_VERTEX_ARRAY);

            GL.DrawArrays(GL.GL_LINES, 0, vertexCount);

            GL.DisableClientState(GL.GL_VERTEX_ARRAY);

            GL.BindBuffer(GL.GL_ARRAY_BUFFER, 0);
        }

        // Draws a sub-range of the buffer's vertices, so one buffer can
        // be drawn in several colours.
        public void DrawRange(int first, int count)
        {
            if (count <= 0) return;

            GL.BindBuffer(GL.GL_ARRAY_BUFFER, vbo);

            GL.VertexPointer(3, GL.GL_FLOAT, Stride, IntPtr.Zero);
            GL.EnableClientState(GL.GL_VERTEX_ARRAY);

            GL.DrawArrays(GL.GL_LINES, first, count);

            GL.DisableClientState(GL.GL_VERTEX_ARRAY);

            GL.BindBuffer(GL.GL_ARRAY_BUFFER, 0);
        }

        public void Dispose()
        {
            GL.DeleteBuffer(vbo);
        }
    }

    internal class ShaderProgram : IDisposable
    {
        private readonly uint handle;

        public bool IsValid => GL.IsProgram(handle);

        public ShaderProgram(string vertexSrc, string fragmentSrc)
        {
            uint vs = CompileShader(GL.GL_VERTEX_SHADER, vertexSrc);
            uint fs = CompileShader(GL.GL_FRAGMENT_SHADER, fragmentSrc);

            handle = GL.CreateProgram();
            GL.AttachShader(handle, vs);
            GL.AttachShader(handle, fs);
            GL.LinkProgram(handle);

            if (GL.GetProgramiv(handle, GL.GL_LINK_STATUS) == 0)
            {
                Debug.Print("Shader link failed: " + GL.GetProgramInfoLog(handle));
            }

            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
        }

        private static uint CompileShader(uint type, string src)
        {
            uint s = GL.CreateShader(type);
            GL.ShaderSource(s, src);
            GL.CompileShader(s);
            if (GL.GetShaderiv(s, GL.GL_COMPILE_STATUS) == 0)
            {
                Debug.Print("Shader compile failed: " + GL.GetShaderInfoLog(s));
            }
            return s;
        }

        public void Use() => GL.UseProgram(handle);

        public void SetFloat(string uniformName, float value)
        {
            int loc = GL.GetUniformLocation(handle, uniformName);
            if (loc == -1) return;
            GL.Uniform1f(loc, value);
        }

        public void SetVec3(string uniformName, float x, float y, float z)
        {
            int loc = GL.GetUniformLocation(handle, uniformName);
            if (loc == -1) return;
            GL.Uniform3f(loc, x, y, z);
        }

        public void Dispose() => GL.DeleteProgram(handle);
    }

    internal class Win32Window : IWin32Window
    {
        public IntPtr Handle { get; }
        public Win32Window(IntPtr handle) { Handle = handle; }
    }

}
