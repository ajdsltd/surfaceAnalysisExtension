using System;
using System.Windows.Forms;

namespace SwIsophoteAddin
{
    // Split into MeshSettingsPanel.cs (this file -- business logic,
    // wired to SwAddin's static state) and MeshSettingsPanel.Designer.cs
    // (control declarations and layout, generated/editable via the VS
    // Form Designer). Event handlers are named methods rather than
    // inline lambdas specifically so the Designer's "double-click a
    // control to jump to its handler" workflow works.
    internal partial class MeshSettingsPanel : Form
    {
        private readonly Action onMeshChanged;
        private readonly Action onSetPlane;
        private readonly Action onSetAxisX;
        private readonly Action onSetAxisY;
        private readonly Action onSetAxisZ;
        private readonly Action onLineChanged;
        private readonly Action onZebraToggled;
        private readonly Action onNormalsToggle;
        private readonly Action onIsolateSelected;
        private readonly Action onClearIsolation;
        private readonly Action onTestEdgeContinuity;
        private readonly Action onCombSettingsChanged;
        private readonly Action onClosedByUser;
        private readonly Action onShowIsocurvesForSelected;

        // WinForms Designer requires a public parameterless constructor
        // to instantiate the form on the design surface -- without this,
        // it can't create an instance at all and just shows an empty
        // Form shell. Delegates are null here since nothing at design
        // time ever fires a real click; the real runtime constructor
        // below is what ConnectToSW always uses.
        public MeshSettingsPanel()
            : this(null, null, null, null, null, null, null, null, null, null, null, null, null, null)
        {
        }

        public MeshSettingsPanel(
            Action onMeshSettingsChanged,
            Action onSetPlaneClicked,
            Action onSetAxisXClicked,
            Action onSetAxisYClicked,
            Action onSetAxisZClicked,
            Action onLineSettingsChanged,
            Action onZebraToggled,
            Action onNormalsToggleClicked,
            Action onIsolateSelectedClicked,
            Action onClearIsolationClicked,
            Action onTestEdgeContinuityClicked,
            Action onCombSettingsChangedClicked,
            Action onClosedByUserClicked,
            Action onShowIsocurvesForSelectedClicked)
        {
            onMeshChanged = onMeshSettingsChanged;
            onSetPlane = onSetPlaneClicked;
            onSetAxisX = onSetAxisXClicked;
            onSetAxisY = onSetAxisYClicked;
            onSetAxisZ = onSetAxisZClicked;
            onLineChanged = onLineSettingsChanged;
            this.onZebraToggled = onZebraToggled;
            onNormalsToggle = onNormalsToggleClicked;
            onIsolateSelected = onIsolateSelectedClicked;
            onClearIsolation = onClearIsolationClicked;
            onTestEdgeContinuity = onTestEdgeContinuityClicked;
            onCombSettingsChanged = onCombSettingsChangedClicked;
            onClosedByUser = onClosedByUserClicked;
            onShowIsocurvesForSelected = onShowIsocurvesForSelectedClicked;

            InitializeComponent();

            // Clicking the window's own X button (or Alt+F4) calls
            // Form.Close(), which by default DISPOSES the form -- the
            // next click of the CommandManager toggle button would then
            // try to Show() a disposed object and throw
            // ObjectDisposedException. Intercepting FormClosing, and
            // only for CloseReason.UserClosing (so DisconnectFromSW's
            // own real Close()/Dispose() during unload still goes
            // through normally, since that's CloseReason.None), cancels
            // the actual close and just hides instead -- keeping the
            // single instance alive for the next Show(), and notifying
            // SwAddin so the toggle button/overlay go back to "off" the
            // same way clicking the toggle button itself would.
            FormClosing += MeshSettingsPanel_FormClosing;

            // Numeric controls get their starting Value from SwAddin's
            // current static state rather than a Designer-time literal,
            // since the add-in's own defaults are the source of truth
            // (and the two happen to already match at cold start, but
            // this keeps it correct if that ever changes).
            meshToleranceInput.Value = (decimal)SwAddin.MeshToleranceMM;
            chordAngleInput.Value = (decimal)SwAddin.ChordAngleDeg;
            lineFrequencyInput.Value = (decimal)SwAddin.LineFrequency;
            lineWidthInput.Value = (decimal)SwAddin.LineWidth;
            lineFeatherInput.Value = (decimal)SwAddin.LineFeather;
            normalLengthInput.Value = (decimal)SwAddin.NormalLengthMM;
            densityInput.Value = SwAddin.EdgeContinuitySampleCount;
            combScaleInput.Value = (decimal)SwAddin.CombScale;
            combToleranceInput.Value = (decimal)SwAddin.CombToleranceAbsPerMM;

            polygonCountTimer.Start();
        }

        private void MeshSettingsPanel_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                onClosedByUser?.Invoke();
            }
        }

        private void isolateSelectedButton_Click(object sender, EventArgs e) => onIsolateSelected?.Invoke();

        private void showAllButton_Click(object sender, EventArgs e) => onClearIsolation?.Invoke();

        private void meshToleranceInput_ValueChanged(object sender, EventArgs e)
        {
            SwAddin.MeshToleranceMM = (double)meshToleranceInput.Value;
            onMeshChanged?.Invoke();
        }

        private void chordAngleInput_ValueChanged(object sender, EventArgs e)
        {
            SwAddin.ChordAngleDeg = (double)chordAngleInput.Value;
            onMeshChanged?.Invoke();
        }

        private void polygonCountTimer_Tick(object sender, EventArgs e)
        {
            polygonCountLabel.Text = "Visible polygons: " + SwAddin.LastVisiblePolygonCount.ToString("N0");
        }

        private void setPlaneButton_Click(object sender, EventArgs e) => onSetPlane?.Invoke();

        private void setAxisXButton_Click(object sender, EventArgs e) => onSetAxisX?.Invoke();

        private void setAxisYButton_Click(object sender, EventArgs e) => onSetAxisY?.Invoke();

        private void setAxisZButton_Click(object sender, EventArgs e) => onSetAxisZ?.Invoke();

        private void matcapAxisButton_Click(object sender, EventArgs e)
        {
            SwAddin.MatcapAxis = (SwAddin.MatcapAxis + 1) % 2;
            matcapAxisButton.Text = "Env. Zebra axis: " + (SwAddin.MatcapAxis == 0 ? "X (Vertical)" : "Y (Horizontal)");
            SwAddin.OverlayMode = 1;
            onZebraToggled?.Invoke();
        }

        private void stripesOnlyToggleButton_Click(object sender, EventArgs e)
        {
            SwAddin.StripesOnly = !SwAddin.StripesOnly;
            stripesOnlyToggleButton.Text = SwAddin.StripesOnly ? "Stripes Only: ON" : "Stripes Only: OFF";
            onZebraToggled?.Invoke();
        }

        private void lineFrequencyInput_ValueChanged(object sender, EventArgs e)
        {
            SwAddin.LineFrequency = (float)lineFrequencyInput.Value;
            onLineChanged?.Invoke();
        }

        private void lineWidthInput_ValueChanged(object sender, EventArgs e)
        {
            SwAddin.LineWidth = (float)lineWidthInput.Value;
            onLineChanged?.Invoke();
        }

        private void lineFeatherInput_ValueChanged(object sender, EventArgs e)
        {
            SwAddin.LineFeather = (float)lineFeatherInput.Value;
            onLineChanged?.Invoke();
        }

        // Isocurve display -- per-face only (the earlier general
        // Isocurves: ON/OFF toggle, which showed isocurves on every
        // face, has been removed per [stated]'s request). Single
        // button: click with a face selection to show isocurves +
        // per-face degree/CV callouts for just those faces; click again
        // (any selection state) to hide both.
        private void showIsocurvesForSelectedButton_Click(object sender, EventArgs e) => onShowIsocurvesForSelected?.Invoke();

        // Same pattern as SyncNormalsButtonState/SyncOverlayButtonStates
        // -- also called from SwAddin's doc-switch reset, not just this
        // button's own click handler.
        public void SyncShowIsocurvesForSelectedButtonState()
        {
            showIsocurvesForSelectedButton.Text = SwAddin.IsIsocurveIsolating
                ? "Show Isocurves for Selected Faces (ON)"
                : "Show Isocurves for Selected Faces";
        }

        private void normalLengthInput_ValueChanged(object sender, EventArgs e)
        {
            SwAddin.NormalLengthMM = (double)normalLengthInput.Value;
        }

        private void normalsToggleButton_Click(object sender, EventArgs e) => onNormalsToggle?.Invoke();

        private void densityInput_ValueChanged(object sender, EventArgs e)
        {
            SwAddin.EdgeContinuitySampleCount = (int)densityInput.Value;
            // Changes the SAMPLING of ComputeEdgeContinuity itself, not
            // just the comb's display -- unlike Scale/Tolerance below,
            // it has no effect until "Test G2 Continuity" is run again
            // (no live-held edge reference to recompute against).
        }

        private void combScaleInput_ValueChanged(object sender, EventArgs e)
        {
            SwAddin.CombScale = (double)combScaleInput.Value;
            onCombSettingsChanged?.Invoke();
        }

        private void combToleranceInput_ValueChanged(object sender, EventArgs e)
        {
            SwAddin.CombToleranceAbsPerMM = (double)combToleranceInput.Value;
            onCombSettingsChanged?.Invoke();
        }

        private void testEdgeContinuityButton_Click(object sender, EventArgs e) => onTestEdgeContinuity?.Invoke();

        // Reflects current mode on whichever of the two dual-purpose
        // buttons (Isolate Selected Faces / Show All) is active --
        // neither shows an ON marker when Overlay is off, since there's
        // no longer a single standalone toggle whose state alone
        // represents "on".
        public void SyncNormalsButtonState()
        {
            normalsToggleButton.Text = SwAddin.NormalsShown ? "Normals: ON" : "Normals: OFF";
        }

        public void SyncOverlayButtonStates()
        {
            bool showingIsolateOn = SwAddin.OverlayEnabled && SwAddin.IsIsolating;
            bool showingAllOn = SwAddin.OverlayEnabled && !SwAddin.IsIsolating;

            isolateSelectedButton.Text = showingIsolateOn ? "Isolate Selected Faces (ON)" : "Isolate Selected Faces";
            showAllButton.Text = showingAllOn ? "Show All (ON)" : "Show All";
        }

        // One line -- status/error messages only. Success no longer
        // repeats the deviation numbers here (redundant now that the
        // on-screen callout shows them directly at the worst point);
        // full detail still goes to Debug.Print/Output as before.
        public void SetG2ContinuityResult(string text)
        {
            if (g2ResultLabel != null) g2ResultLabel.Text = text;
        }

        private void MeshSettingsPanel_Load(object sender, EventArgs e)
        {

        }

        private void curvatureScaleLabel_Click(object sender, EventArgs e)
        {

        }

        private void label1_Click(object sender, EventArgs e)
        {

        }

        private void edgeBlurLabel_Click(object sender, EventArgs e)
        {

        }

        private void normalLengthLabel_Click(object sender, EventArgs e)
        {

        }

        private void label4_Click(object sender, EventArgs e)
        {

        }

        private void separatorPanel1_Paint(object sender, PaintEventArgs e)
        {

        }

        private void meshToleranceLabel_Click(object sender, EventArgs e)
        {

        }

        private void meshToleranceLabel_Click_1(object sender, EventArgs e)
        {

        }
    }
}
