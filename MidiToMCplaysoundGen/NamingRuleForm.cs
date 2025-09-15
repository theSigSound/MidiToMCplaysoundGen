using System;
using System.Windows.Forms;

namespace MidiToMCplaysoundGen
{
    public partial class NamingRuleForm : Form
    {
        // 命名規則用クラス
        public class NamingRule
        {
            public string Low2Name { get; set; }
            public string Low1Name { get; set; }
            public string MidName { get; set; }
            public string High1Name { get; set; }
            public string High2Name { get; set; }
        }

        // 外部から取得できるプロパティ
        public NamingRule Rule { get; private set; }

        // 新しいコンストラクタ：NamingRuleを受け取れる
        public NamingRuleForm(string baseName, NamingRule initialRule = null)
        {
            InitializeComponent();
            if (initialRule != null)
            {
                // 前回の内容を反映
                textBox5.Text = initialRule.Low2Name;
                textBox4.Text = initialRule.Low1Name;
                textBox3.Text = initialRule.MidName;
                textBox2.Text = initialRule.High1Name;
                textBox1.Text = initialRule.High2Name;
            }
            else
            {
                // デフォルト
                AutoFillNames(baseName);
            }
        }

        private void AutoFillNames(string baseName)
        {
            textBox5.Text = baseName + "_low2";
            textBox4.Text = baseName + "_low1";
            textBox3.Text = baseName;
            textBox2.Text = baseName + "_high1";
            textBox1.Text = baseName + "_high2";
        }

        private void button2_Click(object sender, EventArgs e)
        {
            Rule = new NamingRule
            {
                Low2Name = textBox5.Text,
                Low1Name = textBox4.Text,
                MidName = textBox3.Text,
                High1Name = textBox2.Text,
                High2Name = textBox1.Text
            };
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }
    }
}
