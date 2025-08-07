using System.Windows.Forms;

namespace SCLOCUA.Forms
{
    partial class KillFeedForm
    {
        private System.ComponentModel.IContainer components = null;
        private System.Windows.Forms.RichTextBox richTextBox1;
        private System.Windows.Forms.Label label_status;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.richTextBox1 = new System.Windows.Forms.RichTextBox();
            this.label_status = new System.Windows.Forms.Label();
            this.SuspendLayout();
            // 
            // richTextBox1
            // 
            this.richTextBox1.BackColor = System.Drawing.Color.FromArgb(6, 71, 124);
            this.richTextBox1.Dock = System.Windows.Forms.DockStyle.Top;
            this.richTextBox1.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.75F, System.Drawing.FontStyle.Bold);
            this.richTextBox1.ForeColor = System.Drawing.Color.Lime;
            this.richTextBox1.Location = new System.Drawing.Point(0, 0);
            this.richTextBox1.Name = "richTextBox1";
            this.richTextBox1.ReadOnly = true;
            this.richTextBox1.Size = new System.Drawing.Size(549, 200);
            this.richTextBox1.TabIndex = 0;
            this.richTextBox1.Text = "";
            // 
            // label_status
            // 
            this.label_status.AutoSize = true;
            this.label_status.ForeColor = System.Drawing.Color.White;
            this.label_status.Location = new System.Drawing.Point(12, 210);
            this.label_status.Name = "label_status";
            this.label_status.Size = new System.Drawing.Size(70, 13);
            this.label_status.TabIndex = 1;
            this.label_status.Text = "Status: idle";
            // 
            // KillFeedForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(14, 40, 58);
            this.ClientSize = new System.Drawing.Size(549, 233);
            this.Controls.Add(this.label_status);
            this.Controls.Add(this.richTextBox1);
            this.Name = "KillFeedForm";
            this.Text = "KillFeed";
            this.Load += new System.EventHandler(this.KillFeedForm_Load);
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.KillFeedForm_FormClosing);
            this.ResumeLayout(false);
            this.PerformLayout();
        }
    }
}
