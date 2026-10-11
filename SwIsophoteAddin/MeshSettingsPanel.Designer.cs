namespace SwIsophoteAddin
{
    partial class MeshSettingsPanel
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                polygonCountTimer?.Stop();
                polygonCountTimer?.Dispose();
                components?.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private System.Windows.Forms.Button isolateSelectedButton;
        private System.Windows.Forms.Button showAllButton;
        private System.Windows.Forms.Label meshToleranceLabel;
        private System.Windows.Forms.NumericUpDown meshToleranceInput;
        private System.Windows.Forms.Label chordAngleLabel;
        private System.Windows.Forms.NumericUpDown chordAngleInput;
        private System.Windows.Forms.Label polygonCountLabel;
        private System.Windows.Forms.Button setPlaneButton;
        private System.Windows.Forms.Button setAxisXButton;
        private System.Windows.Forms.Button setAxisYButton;
        private System.Windows.Forms.Button setAxisZButton;
        private System.Windows.Forms.Button matcapAxisButton;
        private System.Windows.Forms.Button stripesOnlyToggleButton;
        private System.Windows.Forms.Label lineCountLabel;
        private System.Windows.Forms.NumericUpDown lineFrequencyInput;
        private System.Windows.Forms.Label lineWidthLabel;
        private System.Windows.Forms.NumericUpDown lineWidthInput;
        private System.Windows.Forms.NumericUpDown lineFeatherInput;
        private System.Windows.Forms.Button showIsocurvesForSelectedButton;
        private System.Windows.Forms.Label normalLengthLabel;
        private System.Windows.Forms.NumericUpDown normalLengthInput;
        private System.Windows.Forms.Button normalsToggleButton;
        private System.Windows.Forms.Panel separatorPanel3;
        private System.Windows.Forms.Label densityLabel;
        private System.Windows.Forms.NumericUpDown densityInput;
        private System.Windows.Forms.Label curvatureScaleLabel;
        private System.Windows.Forms.NumericUpDown combScaleInput;
        private System.Windows.Forms.Label curvatureToleranceLabel;
        private System.Windows.Forms.NumericUpDown combToleranceInput;
        private System.Windows.Forms.Button testEdgeContinuityButton;
        private System.Windows.Forms.Label g2ResultLabel;
        private System.Windows.Forms.Timer polygonCountTimer;

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.isolateSelectedButton = new System.Windows.Forms.Button();
            this.showAllButton = new System.Windows.Forms.Button();
            this.meshToleranceLabel = new System.Windows.Forms.Label();
            this.meshToleranceInput = new System.Windows.Forms.NumericUpDown();
            this.chordAngleLabel = new System.Windows.Forms.Label();
            this.chordAngleInput = new System.Windows.Forms.NumericUpDown();
            this.polygonCountLabel = new System.Windows.Forms.Label();
            this.setPlaneButton = new System.Windows.Forms.Button();
            this.setAxisXButton = new System.Windows.Forms.Button();
            this.setAxisYButton = new System.Windows.Forms.Button();
            this.setAxisZButton = new System.Windows.Forms.Button();
            this.matcapAxisButton = new System.Windows.Forms.Button();
            this.stripesOnlyToggleButton = new System.Windows.Forms.Button();
            this.lineCountLabel = new System.Windows.Forms.Label();
            this.lineFrequencyInput = new System.Windows.Forms.NumericUpDown();
            this.lineWidthLabel = new System.Windows.Forms.Label();
            this.lineWidthInput = new System.Windows.Forms.NumericUpDown();
            this.lineFeatherInput = new System.Windows.Forms.NumericUpDown();
            this.showIsocurvesForSelectedButton = new System.Windows.Forms.Button();
            this.normalLengthLabel = new System.Windows.Forms.Label();
            this.normalLengthInput = new System.Windows.Forms.NumericUpDown();
            this.normalsToggleButton = new System.Windows.Forms.Button();
            this.separatorPanel3 = new System.Windows.Forms.Panel();
            this.densityLabel = new System.Windows.Forms.Label();
            this.densityInput = new System.Windows.Forms.NumericUpDown();
            this.curvatureScaleLabel = new System.Windows.Forms.Label();
            this.combScaleInput = new System.Windows.Forms.NumericUpDown();
            this.curvatureToleranceLabel = new System.Windows.Forms.Label();
            this.combToleranceInput = new System.Windows.Forms.NumericUpDown();
            this.testEdgeContinuityButton = new System.Windows.Forms.Button();
            this.g2ResultLabel = new System.Windows.Forms.Label();
            this.polygonCountTimer = new System.Windows.Forms.Timer(this.components);
            this.label1 = new System.Windows.Forms.Label();
            this.edgeBlurLabel = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.separatorPanel1 = new System.Windows.Forms.Panel();
            this.separatorPanel2 = new System.Windows.Forms.Panel();
            this.label4 = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.meshToleranceInput)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.chordAngleInput)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.lineFrequencyInput)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.lineWidthInput)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.lineFeatherInput)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.normalLengthInput)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.densityInput)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.combScaleInput)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.combToleranceInput)).BeginInit();
            this.SuspendLayout();
            // 
            // isolateSelectedButton
            // 
            this.isolateSelectedButton.Location = new System.Drawing.Point(8, 56);
            this.isolateSelectedButton.Name = "isolateSelectedButton";
            this.isolateSelectedButton.Size = new System.Drawing.Size(180, 26);
            this.isolateSelectedButton.TabIndex = 0;
            this.isolateSelectedButton.Text = "Isolate Selected Faces";
            this.isolateSelectedButton.UseVisualStyleBackColor = true;
            this.isolateSelectedButton.Click += new System.EventHandler(this.isolateSelectedButton_Click);
            // 
            // showAllButton
            // 
            this.showAllButton.Location = new System.Drawing.Point(8, 24);
            this.showAllButton.Name = "showAllButton";
            this.showAllButton.Size = new System.Drawing.Size(180, 26);
            this.showAllButton.TabIndex = 1;
            this.showAllButton.Text = "Show All";
            this.showAllButton.UseVisualStyleBackColor = true;
            this.showAllButton.Click += new System.EventHandler(this.showAllButton_Click);
            // 
            // meshToleranceLabel
            // 
            this.meshToleranceLabel.AutoSize = true;
            this.meshToleranceLabel.Location = new System.Drawing.Point(8, 90);
            this.meshToleranceLabel.Name = "meshToleranceLabel";
            this.meshToleranceLabel.Size = new System.Drawing.Size(84, 13);
            this.meshToleranceLabel.TabIndex = 2;
            this.meshToleranceLabel.Text = "Mesh Tolerance";
            this.meshToleranceLabel.Click += new System.EventHandler(this.meshToleranceLabel_Click_1);
            // 
            // meshToleranceInput
            // 
            this.meshToleranceInput.DecimalPlaces = 4;
            this.meshToleranceInput.Increment = new decimal(new int[] {
            10,
            0,
            0,
            262144});
            this.meshToleranceInput.Location = new System.Drawing.Point(118, 88);
            this.meshToleranceInput.Maximum = new decimal(new int[] {
            50000,
            0,
            0,
            262144});
            this.meshToleranceInput.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            262144});
            this.meshToleranceInput.Name = "meshToleranceInput";
            this.meshToleranceInput.Size = new System.Drawing.Size(70, 20);
            this.meshToleranceInput.TabIndex = 3;
            this.meshToleranceInput.Value = new decimal(new int[] {
            1000,
            0,
            0,
            262144});
            this.meshToleranceInput.ValueChanged += new System.EventHandler(this.meshToleranceInput_ValueChanged);
            // 
            // chordAngleLabel
            // 
            this.chordAngleLabel.AutoSize = true;
            this.chordAngleLabel.Location = new System.Drawing.Point(8, 114);
            this.chordAngleLabel.Name = "chordAngleLabel";
            this.chordAngleLabel.Size = new System.Drawing.Size(65, 13);
            this.chordAngleLabel.TabIndex = 21;
            this.chordAngleLabel.Text = "Chord Angle";
            // 
            // chordAngleInput
            // 
            this.chordAngleInput.DecimalPlaces = 1;
            this.chordAngleInput.Increment = new decimal(new int[] {
            5,
            0,
            0,
            65536});
            this.chordAngleInput.Location = new System.Drawing.Point(118, 112);
            this.chordAngleInput.Maximum = new decimal(new int[] {
            900,
            0,
            0,
            65536});
            this.chordAngleInput.Minimum = new decimal(new int[] {
            5,
            0,
            0,
            65536});
            this.chordAngleInput.Name = "chordAngleInput";
            this.chordAngleInput.Size = new System.Drawing.Size(70, 20);
            this.chordAngleInput.TabIndex = 22;
            this.chordAngleInput.Value = new decimal(new int[] {
            100,
            0,
            0,
            65536});
            this.chordAngleInput.ValueChanged += new System.EventHandler(this.chordAngleInput_ValueChanged);
            // 
            // polygonCountLabel
            // 
            this.polygonCountLabel.AutoSize = true;
            this.polygonCountLabel.Location = new System.Drawing.Point(8, 136);
            this.polygonCountLabel.Name = "polygonCountLabel";
            this.polygonCountLabel.Size = new System.Drawing.Size(94, 13);
            this.polygonCountLabel.TabIndex = 4;
            this.polygonCountLabel.Text = "Visible polygons: 0";
            // 
            // setPlaneButton
            // 
            this.setPlaneButton.Location = new System.Drawing.Point(8, 152);
            this.setPlaneButton.Name = "setPlaneButton";
            this.setPlaneButton.Size = new System.Drawing.Size(180, 26);
            this.setPlaneButton.TabIndex = 5;
            this.setPlaneButton.Text = "Set Plane (parallel to screen)";
            this.setPlaneButton.UseVisualStyleBackColor = true;
            this.setPlaneButton.Click += new System.EventHandler(this.setPlaneButton_Click);
            // 
            // setAxisXButton
            // 
            this.setAxisXButton.Location = new System.Drawing.Point(8, 184);
            this.setAxisXButton.Name = "setAxisXButton";
            this.setAxisXButton.Size = new System.Drawing.Size(50, 26);
            this.setAxisXButton.TabIndex = 6;
            this.setAxisXButton.Text = "Set X";
            this.setAxisXButton.UseVisualStyleBackColor = true;
            this.setAxisXButton.Click += new System.EventHandler(this.setAxisXButton_Click);
            // 
            // setAxisYButton
            // 
            this.setAxisYButton.Location = new System.Drawing.Point(73, 184);
            this.setAxisYButton.Name = "setAxisYButton";
            this.setAxisYButton.Size = new System.Drawing.Size(50, 26);
            this.setAxisYButton.TabIndex = 7;
            this.setAxisYButton.Text = "Set Y";
            this.setAxisYButton.UseVisualStyleBackColor = true;
            this.setAxisYButton.Click += new System.EventHandler(this.setAxisYButton_Click);
            // 
            // setAxisZButton
            // 
            this.setAxisZButton.Location = new System.Drawing.Point(138, 184);
            this.setAxisZButton.Name = "setAxisZButton";
            this.setAxisZButton.Size = new System.Drawing.Size(50, 26);
            this.setAxisZButton.TabIndex = 8;
            this.setAxisZButton.Text = "Set Z";
            this.setAxisZButton.UseVisualStyleBackColor = true;
            this.setAxisZButton.Click += new System.EventHandler(this.setAxisZButton_Click);
            // 
            // matcapAxisButton
            // 
            this.matcapAxisButton.Location = new System.Drawing.Point(8, 216);
            this.matcapAxisButton.Name = "matcapAxisButton";
            this.matcapAxisButton.Size = new System.Drawing.Size(180, 26);
            this.matcapAxisButton.TabIndex = 9;
            this.matcapAxisButton.Text = "Env. Zebra axis: Y (Horizontal)";
            this.matcapAxisButton.UseVisualStyleBackColor = true;
            this.matcapAxisButton.Click += new System.EventHandler(this.matcapAxisButton_Click);
            // 
            // stripesOnlyToggleButton
            // 
            this.stripesOnlyToggleButton.Location = new System.Drawing.Point(8, 248);
            this.stripesOnlyToggleButton.Name = "stripesOnlyToggleButton";
            this.stripesOnlyToggleButton.Size = new System.Drawing.Size(180, 26);
            this.stripesOnlyToggleButton.TabIndex = 10;
            this.stripesOnlyToggleButton.Text = "Stripes Only: OFF";
            this.stripesOnlyToggleButton.UseVisualStyleBackColor = true;
            this.stripesOnlyToggleButton.Click += new System.EventHandler(this.stripesOnlyToggleButton_Click);
            // 
            // lineCountLabel
            // 
            this.lineCountLabel.AutoSize = true;
            this.lineCountLabel.Location = new System.Drawing.Point(8, 282);
            this.lineCountLabel.Name = "lineCountLabel";
            this.lineCountLabel.Size = new System.Drawing.Size(60, 13);
            this.lineCountLabel.TabIndex = 11;
            this.lineCountLabel.Text = "Line count:";
            // 
            // lineFrequencyInput
            // 
            this.lineFrequencyInput.Location = new System.Drawing.Point(118, 280);
            this.lineFrequencyInput.Maximum = new decimal(new int[] {
            60,
            0,
            0,
            0});
            this.lineFrequencyInput.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.lineFrequencyInput.Name = "lineFrequencyInput";
            this.lineFrequencyInput.Size = new System.Drawing.Size(70, 20);
            this.lineFrequencyInput.TabIndex = 12;
            this.lineFrequencyInput.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.lineFrequencyInput.ValueChanged += new System.EventHandler(this.lineFrequencyInput_ValueChanged);
            // 
            // lineWidthLabel
            // 
            this.lineWidthLabel.AutoSize = true;
            this.lineWidthLabel.Location = new System.Drawing.Point(8, 306);
            this.lineWidthLabel.Name = "lineWidthLabel";
            this.lineWidthLabel.Size = new System.Drawing.Size(58, 13);
            this.lineWidthLabel.TabIndex = 13;
            this.lineWidthLabel.Text = "Line width:";
            // 
            // lineWidthInput
            // 
            this.lineWidthInput.DecimalPlaces = 2;
            this.lineWidthInput.Increment = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.lineWidthInput.Location = new System.Drawing.Point(118, 304);
            this.lineWidthInput.Maximum = new decimal(new int[] {
            50,
            0,
            0,
            131072});
            this.lineWidthInput.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.lineWidthInput.Name = "lineWidthInput";
            this.lineWidthInput.Size = new System.Drawing.Size(70, 20);
            this.lineWidthInput.TabIndex = 14;
            this.lineWidthInput.Value = new decimal(new int[] {
            1,
            0,
            0,
            131072});
            this.lineWidthInput.ValueChanged += new System.EventHandler(this.lineWidthInput_ValueChanged);
            // 
            // lineFeatherInput
            // 
            this.lineFeatherInput.DecimalPlaces = 1;
            this.lineFeatherInput.Increment = new decimal(new int[] {
            5,
            0,
            0,
            65536});
            this.lineFeatherInput.Location = new System.Drawing.Point(118, 328);
            this.lineFeatherInput.Maximum = new decimal(new int[] {
            20,
            0,
            0,
            0});
            this.lineFeatherInput.Name = "lineFeatherInput";
            this.lineFeatherInput.Size = new System.Drawing.Size(70, 20);
            this.lineFeatherInput.TabIndex = 16;
            this.lineFeatherInput.ValueChanged += new System.EventHandler(this.lineFeatherInput_ValueChanged);
            // 
            // showIsocurvesForSelectedButton
            // 
            this.showIsocurvesForSelectedButton.Location = new System.Drawing.Point(8, 376);
            this.showIsocurvesForSelectedButton.Name = "showIsocurvesForSelectedButton";
            this.showIsocurvesForSelectedButton.Size = new System.Drawing.Size(180, 52);
            this.showIsocurvesForSelectedButton.TabIndex = 18;
            this.showIsocurvesForSelectedButton.Text = "Show Isocurves for Selected Faces";
            this.showIsocurvesForSelectedButton.UseVisualStyleBackColor = true;
            this.showIsocurvesForSelectedButton.Click += new System.EventHandler(this.showIsocurvesForSelectedButton_Click);
            // 
            // normalLengthLabel
            // 
            this.normalLengthLabel.AutoSize = true;
            this.normalLengthLabel.Location = new System.Drawing.Point(6, 492);
            this.normalLengthLabel.Name = "normalLengthLabel";
            this.normalLengthLabel.Size = new System.Drawing.Size(72, 13);
            this.normalLengthLabel.TabIndex = 20;
            this.normalLengthLabel.Text = "Normal length";
            this.normalLengthLabel.Click += new System.EventHandler(this.normalLengthLabel_Click);
            // 
            // normalLengthInput
            // 
            this.normalLengthInput.DecimalPlaces = 1;
            this.normalLengthInput.Increment = new decimal(new int[] {
            5,
            0,
            0,
            65536});
            this.normalLengthInput.Location = new System.Drawing.Point(118, 490);
            this.normalLengthInput.Maximum = new decimal(new int[] {
            500,
            0,
            0,
            0});
            this.normalLengthInput.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.normalLengthInput.Name = "normalLengthInput";
            this.normalLengthInput.Size = new System.Drawing.Size(70, 20);
            this.normalLengthInput.TabIndex = 21;
            this.normalLengthInput.Value = new decimal(new int[] {
            1,
            0,
            0,
            65536});
            this.normalLengthInput.ValueChanged += new System.EventHandler(this.normalLengthInput_ValueChanged);
            // 
            // normalsToggleButton
            // 
            this.normalsToggleButton.Location = new System.Drawing.Point(8, 458);
            this.normalsToggleButton.Name = "normalsToggleButton";
            this.normalsToggleButton.Size = new System.Drawing.Size(180, 26);
            this.normalsToggleButton.TabIndex = 22;
            this.normalsToggleButton.Text = "Normals: OFF";
            this.normalsToggleButton.UseVisualStyleBackColor = true;
            this.normalsToggleButton.Click += new System.EventHandler(this.normalsToggleButton_Click);
            // 
            // separatorPanel3
            // 
            this.separatorPanel3.BackColor = System.Drawing.Color.Silver;
            this.separatorPanel3.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.separatorPanel3.Location = new System.Drawing.Point(8, 514);
            this.separatorPanel3.Name = "separatorPanel3";
            this.separatorPanel3.Size = new System.Drawing.Size(180, 2);
            this.separatorPanel3.TabIndex = 24;
            // 
            // densityLabel
            // 
            this.densityLabel.AutoSize = true;
            this.densityLabel.Location = new System.Drawing.Point(8, 572);
            this.densityLabel.Name = "densityLabel";
            this.densityLabel.Size = new System.Drawing.Size(45, 13);
            this.densityLabel.TabIndex = 25;
            this.densityLabel.Text = "Density:";
            // 
            // densityInput
            // 
            this.densityInput.Location = new System.Drawing.Point(118, 570);
            this.densityInput.Maximum = new decimal(new int[] {
            200,
            0,
            0,
            0});
            this.densityInput.Minimum = new decimal(new int[] {
            2,
            0,
            0,
            0});
            this.densityInput.Name = "densityInput";
            this.densityInput.Size = new System.Drawing.Size(70, 20);
            this.densityInput.TabIndex = 26;
            this.densityInput.Value = new decimal(new int[] {
            2,
            0,
            0,
            0});
            this.densityInput.ValueChanged += new System.EventHandler(this.densityInput_ValueChanged);
            // 
            // curvatureScaleLabel
            // 
            this.curvatureScaleLabel.AutoSize = true;
            this.curvatureScaleLabel.Location = new System.Drawing.Point(8, 596);
            this.curvatureScaleLabel.Name = "curvatureScaleLabel";
            this.curvatureScaleLabel.Size = new System.Drawing.Size(37, 13);
            this.curvatureScaleLabel.TabIndex = 27;
            this.curvatureScaleLabel.Text = "Scale:";
            this.curvatureScaleLabel.Click += new System.EventHandler(this.curvatureScaleLabel_Click);
            // 
            // combScaleInput
            // 
            this.combScaleInput.Increment = new decimal(new int[] {
            50,
            0,
            0,
            0});
            this.combScaleInput.Location = new System.Drawing.Point(118, 594);
            this.combScaleInput.Maximum = new decimal(new int[] {
            20000,
            0,
            0,
            0});
            this.combScaleInput.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.combScaleInput.Name = "combScaleInput";
            this.combScaleInput.Size = new System.Drawing.Size(70, 20);
            this.combScaleInput.TabIndex = 28;
            this.combScaleInput.Value = new decimal(new int[] {
            1,
            0,
            0,
            0});
            this.combScaleInput.ValueChanged += new System.EventHandler(this.combScaleInput_ValueChanged);
            // 
            // curvatureToleranceLabel
            // 
            this.curvatureToleranceLabel.AutoSize = true;
            this.curvatureToleranceLabel.Location = new System.Drawing.Point(8, 620);
            this.curvatureToleranceLabel.Name = "curvatureToleranceLabel";
            this.curvatureToleranceLabel.Size = new System.Drawing.Size(58, 13);
            this.curvatureToleranceLabel.TabIndex = 29;
            this.curvatureToleranceLabel.Text = "Tolerance:\r\n";
            // 
            // combToleranceInput
            // 
            this.combToleranceInput.DecimalPlaces = 3;
            this.combToleranceInput.Increment = new decimal(new int[] {
            1,
            0,
            0,
            196608});
            this.combToleranceInput.Location = new System.Drawing.Point(118, 618);
            this.combToleranceInput.Maximum = new decimal(new int[] {
            5,
            0,
            0,
            0});
            this.combToleranceInput.Minimum = new decimal(new int[] {
            1,
            0,
            0,
            196608});
            this.combToleranceInput.Name = "combToleranceInput";
            this.combToleranceInput.Size = new System.Drawing.Size(70, 20);
            this.combToleranceInput.TabIndex = 30;
            this.combToleranceInput.Value = new decimal(new int[] {
            1,
            0,
            0,
            196608});
            this.combToleranceInput.ValueChanged += new System.EventHandler(this.combToleranceInput_ValueChanged);
            // 
            // testEdgeContinuityButton
            // 
            this.testEdgeContinuityButton.Location = new System.Drawing.Point(8, 538);
            this.testEdgeContinuityButton.Name = "testEdgeContinuityButton";
            this.testEdgeContinuityButton.Size = new System.Drawing.Size(180, 26);
            this.testEdgeContinuityButton.TabIndex = 31;
            this.testEdgeContinuityButton.Text = "Test G2 Continuity ";
            this.testEdgeContinuityButton.UseVisualStyleBackColor = true;
            this.testEdgeContinuityButton.Click += new System.EventHandler(this.testEdgeContinuityButton_Click);
            // 
            // g2ResultLabel
            // 
            this.g2ResultLabel.Location = new System.Drawing.Point(8, 642);
            this.g2ResultLabel.Name = "g2ResultLabel";
            this.g2ResultLabel.Size = new System.Drawing.Size(199, 32);
            this.g2ResultLabel.TabIndex = 32;
            this.g2ResultLabel.Text = "No test run yet.";
            // 
            // polygonCountTimer
            // 
            this.polygonCountTimer.Interval = 300;
            this.polygonCountTimer.Tick += new System.EventHandler(this.polygonCountTimer_Tick);
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label1.Location = new System.Drawing.Point(8, 8);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(183, 13);
            this.label1.TabIndex = 33;
            this.label1.Text = "Zebra | Isophote Analysis 1.0.2";
            this.label1.Click += new System.EventHandler(this.label1_Click);
            // 
            // edgeBlurLabel
            // 
            this.edgeBlurLabel.AutoSize = true;
            this.edgeBlurLabel.Location = new System.Drawing.Point(8, 330);
            this.edgeBlurLabel.Name = "edgeBlurLabel";
            this.edgeBlurLabel.Size = new System.Drawing.Size(55, 13);
            this.edgeBlurLabel.TabIndex = 15;
            this.edgeBlurLabel.Text = "Edge blur:";
            this.edgeBlurLabel.Click += new System.EventHandler(this.edgeBlurLabel_Click);
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label2.Location = new System.Drawing.Point(50, 360);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(97, 13);
            this.label2.TabIndex = 34;
            this.label2.Text = "Show Isocurves";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label3.Location = new System.Drawing.Point(38, 442);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(129, 13);
            this.label3.TabIndex = 35;
            this.label3.Text = "Show Surface Normal";
            // 
            // separatorPanel1
            // 
            this.separatorPanel1.BackColor = System.Drawing.Color.Silver;
            this.separatorPanel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.separatorPanel1.Location = new System.Drawing.Point(8, 352);
            this.separatorPanel1.Name = "separatorPanel1";
            this.separatorPanel1.Size = new System.Drawing.Size(180, 2);
            this.separatorPanel1.TabIndex = 17;
            this.separatorPanel1.Paint += new System.Windows.Forms.PaintEventHandler(this.separatorPanel1_Paint);
            // 
            // separatorPanel2
            // 
            this.separatorPanel2.BackColor = System.Drawing.Color.Silver;
            this.separatorPanel2.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.separatorPanel2.Location = new System.Drawing.Point(8, 434);
            this.separatorPanel2.Name = "separatorPanel2";
            this.separatorPanel2.Size = new System.Drawing.Size(180, 2);
            this.separatorPanel2.TabIndex = 19;
            // 
            // label4
            // 
            this.label4.AutoSize = true;
            this.label4.BackColor = System.Drawing.SystemColors.Control;
            this.label4.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.label4.Location = new System.Drawing.Point(41, 522);
            this.label4.Name = "label4";
            this.label4.Size = new System.Drawing.Size(123, 13);
            this.label4.TabIndex = 36;
            this.label4.Text = "Check G2 Continuity";
            this.label4.Click += new System.EventHandler(this.label4_Click);
            // 
            // MeshSettingsPanel
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(199, 691);
            this.Controls.Add(this.label4);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.isolateSelectedButton);
            this.Controls.Add(this.showAllButton);
            this.Controls.Add(this.meshToleranceLabel);
            this.Controls.Add(this.meshToleranceInput);
            this.Controls.Add(this.chordAngleLabel);
            this.Controls.Add(this.chordAngleInput);
            this.Controls.Add(this.polygonCountLabel);
            this.Controls.Add(this.setPlaneButton);
            this.Controls.Add(this.setAxisXButton);
            this.Controls.Add(this.setAxisYButton);
            this.Controls.Add(this.setAxisZButton);
            this.Controls.Add(this.matcapAxisButton);
            this.Controls.Add(this.stripesOnlyToggleButton);
            this.Controls.Add(this.lineCountLabel);
            this.Controls.Add(this.lineFrequencyInput);
            this.Controls.Add(this.lineWidthLabel);
            this.Controls.Add(this.lineWidthInput);
            this.Controls.Add(this.edgeBlurLabel);
            this.Controls.Add(this.lineFeatherInput);
            this.Controls.Add(this.separatorPanel1);
            this.Controls.Add(this.showIsocurvesForSelectedButton);
            this.Controls.Add(this.separatorPanel2);
            this.Controls.Add(this.normalLengthLabel);
            this.Controls.Add(this.normalLengthInput);
            this.Controls.Add(this.normalsToggleButton);
            this.Controls.Add(this.separatorPanel3);
            this.Controls.Add(this.densityLabel);
            this.Controls.Add(this.densityInput);
            this.Controls.Add(this.curvatureScaleLabel);
            this.Controls.Add(this.combScaleInput);
            this.Controls.Add(this.curvatureToleranceLabel);
            this.Controls.Add(this.combToleranceInput);
            this.Controls.Add(this.testEdgeContinuityButton);
            this.Controls.Add(this.g2ResultLabel);
            this.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow;
            this.Location = new System.Drawing.Point(40, 40);
            this.Name = "MeshSettingsPanel";
            this.ShowInTaskbar = false;
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "Surface Analysis";
            this.Load += new System.EventHandler(this.MeshSettingsPanel_Load);
            ((System.ComponentModel.ISupportInitialize)(this.meshToleranceInput)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.chordAngleInput)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.lineFrequencyInput)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.lineWidthInput)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.lineFeatherInput)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.normalLengthInput)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.densityInput)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.combScaleInput)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.combToleranceInput)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label edgeBlurLabel;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Panel separatorPanel1;
        private System.Windows.Forms.Panel separatorPanel2;
        private System.Windows.Forms.Label label4;
    }
}