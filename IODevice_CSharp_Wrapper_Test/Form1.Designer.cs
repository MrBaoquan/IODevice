namespace IODevice_C_CSharpTest
{
    partial class Form1
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.axisLabel = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.actionLabel = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.keyLabel = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // timer1
            // 
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // axisLabel
            // 
            this.axisLabel.AutoSize = true;
            this.axisLabel.Font = new System.Drawing.Font("宋体", 15F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.axisLabel.Location = new System.Drawing.Point(516, 237);
            this.axisLabel.Name = "axisLabel";
            this.axisLabel.Size = new System.Drawing.Size(39, 20);
            this.axisLabel.TabIndex = 0;
            this.axisLabel.Text = "0.0";
            // 
            // label1
            // 
            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("宋体", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label1.Location = new System.Drawing.Point(516, 204);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(99, 19);
            this.label1.TabIndex = 1;
            this.label1.Text = "BindAxis:";
            // 
            // label2
            // 
            this.label2.AutoSize = true;
            this.label2.Font = new System.Drawing.Font("宋体", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label2.Location = new System.Drawing.Point(311, 204);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(119, 19);
            this.label2.TabIndex = 3;
            this.label2.Text = "BindAction:";
            // 
            // actionLabel
            // 
            this.actionLabel.AutoSize = true;
            this.actionLabel.Font = new System.Drawing.Font("宋体", 15F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.actionLabel.Location = new System.Drawing.Point(311, 237);
            this.actionLabel.Name = "actionLabel";
            this.actionLabel.Size = new System.Drawing.Size(79, 20);
            this.actionLabel.TabIndex = 2;
            this.actionLabel.Text = "Default";
            // 
            // label3
            // 
            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("宋体", 14.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.label3.Location = new System.Drawing.Point(112, 204);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(89, 19);
            this.label3.TabIndex = 5;
            this.label3.Text = "BindKey:";
            // 
            // keyLabel
            // 
            this.keyLabel.AutoSize = true;
            this.keyLabel.Font = new System.Drawing.Font("宋体", 15F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
            this.keyLabel.Location = new System.Drawing.Point(112, 237);
            this.keyLabel.Name = "keyLabel";
            this.keyLabel.Size = new System.Drawing.Size(79, 20);
            this.keyLabel.TabIndex = 4;
            this.keyLabel.Text = "Default";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(734, 434);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.keyLabel);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.actionLabel);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.axisLabel);
            this.Name = "Form1";
            this.Text = "Form1";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.Label axisLabel;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label actionLabel;
        private System.Windows.Forms.Label label3;
        private System.Windows.Forms.Label keyLabel;
    }
}

