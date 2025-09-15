using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Newtonsoft.Json;
using NAudio.Midi;

namespace MidiToMCplaysoundGen
{
    public partial class Form1 : Form
    {
        private List<string> instruments = new List<string>();
        private NamingRuleForm.NamingRule namingRule = null;

        private static readonly UTF8Encoding utf8NoBom = new UTF8Encoding(false);

        private const string LastOutputDirKey = "LastOutputDir";

        private const int CC_RPN_MSB = 101;
        private const int CC_RPN_LSB = 100;
        private const int CC_DATA_ENTRY_MSB = 6;

        private readonly string[] soundSources = new string[]
        {
            "ambient",
            "block",
            "hostile",
            "master",
            "music",
            "neutral",
            "player",
            "record",
            "voice",
            "weather"
        };

        public Form1()
        {
            InitializeComponent();
            textBox5.Text = "8";
            textBox6.Text = "";

            comboBox3.Items.Clear();
            comboBox3.Items.Add("1.13～1.20");
            comboBox3.Items.Add("1.21～");
            comboBox3.SelectedIndex = 0;
            comboBox3.DropDownStyle = ComboBoxStyle.DropDownList;

            LoadInstrumentsFromJson();

            comboBox1.DropDownStyle = ComboBoxStyle.DropDown;
            comboBox1.Items.Clear();
            comboBox1.Items.AddRange(instruments.ToArray());
            int harpIndex = instruments.IndexOf("block.note_block.harp");
            if (harpIndex >= 0)
                comboBox1.SelectedIndex = harpIndex;
            else if (comboBox1.Items.Count > 0)
                comboBox1.SelectedIndex = 0;

            comboBox2.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox2.Items.Clear();

            comboBox1.SelectedIndexChanged += comboBox1_SelectedIndexChanged;
            comboBox1.TextChanged += comboBox1_TextChanged;
            UpdateNamingRuleFromInstrument();

            comboBox4.DropDownStyle = ComboBoxStyle.DropDown;
            UpdateSoundSourceList(false);

            checkBox1.CheckedChanged += checkBoxStopSound_CheckedChanged;

            comboBox5.Items.Clear();
            comboBox5.Items.Add("1");
            comboBox5.Items.Add("2");
            comboBox5.SelectedIndex = 0;
            comboBox5.DropDownStyle = ComboBoxStyle.DropDown;

            string lastDir = Properties.Settings.Default[LastOutputDirKey] as string;
            if (!string.IsNullOrEmpty(lastDir) && Directory.Exists(lastDir))
            {
                textBox2.Text = lastDir;
            }
        }

        private void checkBoxStopSound_CheckedChanged(object sender, EventArgs e)
        {
            UpdateSoundSourceList(checkBox1.Checked);
        }

        private void UpdateSoundSourceList(bool stopsoundMode)
        {
            string current = comboBox4.Text;
            comboBox4.Items.Clear();
            if (stopsoundMode)
            {
                comboBox4.Items.Add("*");
            }
            comboBox4.Items.AddRange(soundSources);
            if (stopsoundMode && current == "*")
                comboBox4.Text = "*";
            else if (soundSources.Contains(current))
                comboBox4.Text = current;
            else
                comboBox4.Text = "record";
        }

        private void LoadInstrumentsFromJson()
        {
            try
            {
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "soundlist.json");
                if (!File.Exists(jsonPath))
                    throw new FileNotFoundException("soundlist.jsonが見つかりません。");

                var json = File.ReadAllText(jsonPath, utf8NoBom);
                instruments = JsonConvert.DeserializeObject<List<string>>(json)
                              ?? new List<string>();
            }
            catch (Exception ex)
            {
                MessageBox.Show("音源リストの読み込みに失敗しました:\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                instruments = new List<string>();
            }
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            UpdateNamingRuleFromInstrument();
        }
        private void comboBox1_TextChanged(object sender, EventArgs e)
        {
            UpdateNamingRuleFromInstrument();
        }

        private void UpdateNamingRuleFromInstrument()
        {
            string selectedInstrument = comboBox1.Text ?? "";
            namingRule = new NamingRuleForm.NamingRule
            {
                Low2Name = selectedInstrument + "_low2",
                Low1Name = selectedInstrument + "_low1",
                MidName = selectedInstrument,
                High1Name = selectedInstrument + "_high1",
                High2Name = selectedInstrument + "_high2"
            };
        }

        private void button1_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "MIDIファイル (*.mid;*.midi)|*.mid;*.midi|すべてのファイル (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    textBox1.Text = ofd.FileName;
                    var channels = GetUsedMidiChannels(ofd.FileName);
                    UpdateChannelComboBox(channels);
                }
            }
        }

        private void button3_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "出力先ディレクトリを選択してください";
                if (fbd.ShowDialog() == DialogResult.OK)
                {
                    textBox2.Text = fbd.SelectedPath;
                    Properties.Settings.Default[LastOutputDirKey] = fbd.SelectedPath;
                    Properties.Settings.Default.Save();
                }
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(textBox1.Text) ||
                string.IsNullOrWhiteSpace(textBox2.Text) ||
                comboBox2.SelectedItem == null ||
                string.IsNullOrWhiteSpace(textBox3.Text) ||
                string.IsNullOrWhiteSpace(textBox4.Text) ||
                string.IsNullOrWhiteSpace(textBox5.Text) ||
                string.IsNullOrWhiteSpace(textBox6.Text) ||
                (!checkBox1.Checked && string.IsNullOrWhiteSpace(comboBox1.Text)))
            {
                MessageBox.Show("すべての項目を入力・選択してください。\n（タグ・名前空間も必須です）", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (checkBox1.Checked && comboBox4.Text.Trim() == "*" && string.IsNullOrWhiteSpace(comboBox1.Text))
            {
                MessageBox.Show("*を選択した際、サウンド名なしでは出力できません。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string tagName = textBox4.Text.Trim();
            string scoreName = textBox3.Text.Trim();
            string selectedInstrument = comboBox1.Text;
            int midiChannel = int.Parse(comboBox2.SelectedItem.ToString().Replace("Ch ", ""));
            int gtPerQuarter;
            if (!int.TryParse(textBox5.Text, out gtPerQuarter) || gtPerQuarter <= 0)
            {
                MessageBox.Show("4分音符あたりのgame tick数は正の整数で入力してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string namespaceName = textBox6.Text.Trim();
            if (string.IsNullOrWhiteSpace(namespaceName) ||
                !Regex.IsMatch(namespaceName, @"^[a-z0-9_]+$"))
            {
                MessageBox.Show("namespaceは英小文字・数字・アンダーバーのみで入力してください。", "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (namingRule == null)
            {
                UpdateNamingRuleFromInstrument();
            }

            try
            {
                GenerateDatapack(
                    textBox1.Text, textBox2.Text,
                    selectedInstrument, midiChannel,
                    tagName, scoreName, namingRule, gtPerQuarter, namespaceName,
                    comboBox4.Text
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show("変換中にエラーが発生しました：\n" + ex.Message, "エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void button4_Click(object sender, EventArgs e)
        {
            var dlg = new NamingRuleForm(comboBox1.Text, namingRule);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                namingRule = dlg.Rule;
            }
        }

        private List<int> GetUsedMidiChannels(string midiFilePath)
        {
            var usedChannels = new HashSet<int>();
            var midiFile = new MidiFile(midiFilePath, false);

            foreach (var track in midiFile.Events)
            {
                foreach (var midiEvent in track)
                {
                    if (midiEvent is NoteOnEvent noteOn && noteOn.Velocity > 0 && noteOn.Channel >= 0 && noteOn.Channel < 16)
                    {
                        usedChannels.Add(noteOn.Channel);
                    }
                }
            }
            var sorted = new List<int>(usedChannels);
            sorted.Sort();
            return sorted;
        }

        private void UpdateChannelComboBox(List<int> channels)
        {
            comboBox2.Items.Clear();
            foreach (var ch in channels)
            {
                comboBox2.Items.Add($"Ch {ch}");
            }
            if (comboBox2.Items.Count > 0)
                comboBox2.SelectedIndex = 0;
        }

        private void GenerateDatapack(
            string midiFilePath,
            string outputDirPath,
            string selectedInstrument,
            int midiChannel,
            string tagName,
            string scoreName,
            NamingRuleForm.NamingRule namingRule,
            int gtPerQuarter,
            string namespaceName,
            string selectedSoundSource
        )
        {
            string versionSelected = comboBox3.SelectedItem?.ToString() ?? "1.13～1.20";
            int packFormat = 41;
            string functionsDirName = "functions";
            string tagDirName = "functions";
            if (versionSelected == "1.21～")
            {
                packFormat = 48;
                functionsDirName = "function";
                tagDirName = "function";
            }

            string datapackRoot = Path.Combine(outputDirPath, $"{namespaceName}_midi_datapack");
            if (!Directory.Exists(datapackRoot))
                Directory.CreateDirectory(datapackRoot);

            string mcmetaPath = Path.Combine(datapackRoot, "pack.mcmeta");
            File.WriteAllText(mcmetaPath,
                "{\n  \"pack\": {\n    \"pack_format\": " + packFormat + ",\n    \"description\": \"Auto-generated MIDI Datapack\"\n  }\n}",
                utf8NoBom);

            string dataDir = Path.Combine(datapackRoot, "data");
            string nsDir = Path.Combine(dataDir, namespaceName);
            string funcDir = Path.Combine(nsDir, functionsDirName);
            string noteDir = Path.Combine(funcDir, "note");
            string treeDir = Path.Combine(funcDir, "tree");
            Directory.CreateDirectory(noteDir);

            if (Directory.Exists(treeDir))
                Directory.Delete(treeDir, true);
            Directory.CreateDirectory(treeDir);

            var midiFile = new MidiFile(midiFilePath, false);
            int ticksPerQuarter = midiFile.DeltaTicksPerQuarterNote;

            var pitchBendTimeline = new List<(long tick, int value)>[16];
            var panTimeline = new List<(long tick, int value)>[16];
            var volumeTimeline = new List<(long tick, int value)>[16];
            for (int ch = 0; ch < 16; ch++)
            {
                pitchBendTimeline[ch] = new List<(long, int)> { (0, 8192) };
                panTimeline[ch] = new List<(long, int)> { (0, 64) };
                volumeTimeline[ch] = new List<(long, int)> { (0, 127) };
            }

            int[] bendRangePerChannel = new int[16];
            for (int ch = 0; ch < 16; ch++) bendRangePerChannel[ch] = 2;
            var rpnState = new (int msb, int lsb)[16];
            for (int ch = 0; ch < 16; ch++) rpnState[ch] = (-1, -1);

            foreach (var track in midiFile.Events)
            {
                foreach (var midiEvent in track)
                {
                    if (midiEvent is ControlChangeEvent cc && cc.Channel >= 0 && cc.Channel < 16)
                    {
                        int ch = cc.Channel;
                        if ((int)cc.Controller == CC_RPN_MSB)
                            rpnState[ch].msb = cc.ControllerValue;
                        else if ((int)cc.Controller == CC_RPN_LSB)
                            rpnState[ch].lsb = cc.ControllerValue;
                        else if ((int)cc.Controller == CC_DATA_ENTRY_MSB)
                        {
                            if (rpnState[ch].msb == 0 && rpnState[ch].lsb == 0)
                                bendRangePerChannel[ch] = cc.ControllerValue;
                        }
                        if ((int)cc.Controller == (int)MidiController.Pan)
                            panTimeline[ch].Add((cc.AbsoluteTime, cc.ControllerValue));
                        else if ((int)cc.Controller == (int)MidiController.MainVolume)
                            volumeTimeline[ch].Add((cc.AbsoluteTime, cc.ControllerValue));
                    }
                    else if (midiEvent is PitchWheelChangeEvent bend && bend.Channel >= 0 && bend.Channel < 16)
                    {
                        pitchBendTimeline[bend.Channel].Add((bend.AbsoluteTime, bend.Pitch));
                    }
                }
            }

            var noteEvents = new List<(long tick, bool isOn, NoteEvent note)>();
            foreach (var track in midiFile.Events)
            {
                foreach (var midiEvent in track)
                {
                    if (midiEvent is NoteOnEvent noteOn && noteOn.Velocity > 0 && noteOn.Channel >= 0 && noteOn.Channel < 16)
                        noteEvents.Add((noteOn.AbsoluteTime, true, noteOn));
                    else if (midiEvent is NoteEvent noteOff &&
                             (noteOff.CommandCode == MidiCommandCode.NoteOff || (noteOff is NoteOnEvent n && n.Velocity == 0)) &&
                             noteOff.Channel >= 0 && noteOff.Channel < 16)
                        noteEvents.Add((noteOff.AbsoluteTime, false, noteOff));
                }
            }
            noteEvents = noteEvents.OrderBy(e => e.tick).ToList();
            var usedNoteOff = new HashSet<NoteEvent>();
            int bendResolutionGt = 1;
            if (!int.TryParse(comboBox5.Text, out bendResolutionGt) || bendResolutionGt <= 0) bendResolutionGt = 1;

            var ranges = new[]
            {
                new { Name = namingRule.Low2Name, Center = 18 },
                new { Name = namingRule.Low1Name, Center = 42 },
                new { Name = namingRule.MidName,  Center = 66 },
                new { Name = namingRule.High1Name, Center = 90 },
                new { Name = namingRule.High2Name, Center = 114 }
            };

            int midCenter = ranges[2].Center;

            var timingDict = new Dictionary<int, List<string>>();
            int outputCount = 0;

            for (int i = 0; i < noteEvents.Count; i++)
            {
                var e = noteEvents[i];
                if (!e.isOn) continue;
                var noteOn = (NoteOnEvent)e.note;
                int channel = noteOn.Channel;
                if (channel != midiChannel) continue;
                int noteNum = noteOn.NoteNumber;
                long startTick = noteOn.AbsoluteTime;
                int velocity = noteOn.Velocity;

                NoteEvent noteOff = null;
                for (int j = i + 1; j < noteEvents.Count; j++)
                {
                    var off = noteEvents[j];
                    if (!off.isOn && !usedNoteOff.Contains(off.note) &&
                        off.note.Channel == channel && off.note.NoteNumber == noteNum &&
                        off.tick >= startTick)
                    {
                        noteOff = off.note;
                        usedNoteOff.Add(off.note);
                        break;
                    }
                }
                long endTick = noteOff != null ? noteOff.AbsoluteTime : startTick + 1;

                bool hasPitchBend = pitchBendTimeline[channel].Any(b => b.tick >= startTick && b.tick < endTick);

                if (!hasPitchBend)
                {
                    int bendRange = bendRangePerChannel[channel];
                    int bendValue = pitchBendTimeline[channel].LastOrDefault(b => b.tick <= startTick).value;
                    double bendSemitone = (bendValue - 8192) * (bendRange / 8192.0);
                    double realNote = noteNum + bendSemitone;

                    int panValue = panTimeline[channel].LastOrDefault(p => p.tick <= startTick).value;

                    double volume = Math.Round(velocity / 127.0, 3);

                    string soundName = null;
                    double pitch = 1.0;
                    double minDiff = double.MaxValue;
                    int bestPriority = int.MaxValue;
                    double minCenterDist = double.MaxValue;
                    for (int idx = 0; idx < ranges.Length; idx++)
                    {
                        var range = ranges[idx];
                        double p = Math.Pow(2.0, (realNote - range.Center) / 12.0);
                        if (p >= 0.5 && p <= 2.0)
                        {
                            double diff = Math.Abs(Math.Log(p, 2));
                            int priority = (range.Name == namingRule.MidName) ? 0 : 1;
                            double centerDist = Math.Abs(range.Center - midCenter);

                            if (
                                diff < minDiff ||
                                (Math.Abs(diff - minDiff) < 1e-8 && priority < bestPriority) ||
                                (Math.Abs(diff - minDiff) < 1e-8 && priority == bestPriority && centerDist < minCenterDist)
                            )
                            {
                                minDiff = diff;
                                bestPriority = priority;
                                minCenterDist = centerDist;
                                soundName = range.Name;
                                pitch = Math.Round(p, 7);
                            }
                        }
                    }
                    if (soundName == null) continue;

                    double panNorm = (panValue - 64) / 64.0;
                    double theta = -panNorm * (Math.PI / 2.0);
                    double x = Math.Round(Math.Sin(theta) * 5.0, 4);
                    double y = Math.Round(Math.Cos(theta) * 5.0, 4);
                    if (y < 0) y = 0;
                    string panStr = $"^{x} ^{y} ^";

                    int timing = (int)Math.Round(startTick * gtPerQuarter / (double)ticksPerQuarter);

                    string playsoundSource = comboBox4.Text == "*" ? "record" : comboBox4.Text;
                    string cmd;

                    if (checkBox1.Checked)
                    {
                        if (string.IsNullOrWhiteSpace(comboBox4.Text))
                        {
                            cmd = "stopsound @s";
                        }
                        else if (string.IsNullOrWhiteSpace(comboBox1.Text))
                        {
                            cmd = $"stopsound @s {comboBox4.Text}";
                        }
                        else
                        {
                            cmd = $"stopsound @s {comboBox4.Text} {comboBox1.Text}";
                        }
                    }
                    else
                    {
                        cmd = $"playsound {soundName} {playsoundSource} @s {panStr} {volume} {pitch}";
                    }

                    string filePath = Path.Combine(noteDir, $"{timing}.mcfunction");
                    List<string> allLines = new List<string>();
                    if (File.Exists(filePath))
                        allLines.AddRange(File.ReadAllLines(filePath, utf8NoBom));
                    allLines.Add(cmd);
                    File.WriteAllLines(filePath, allLines, utf8NoBom);

                    if (!timingDict.ContainsKey(timing))
                        timingDict[timing] = new List<string>();
                    timingDict[timing].Add(cmd);

                    outputCount++;
                }
                else
                {
                    int bendRange = bendRangePerChannel[channel];
                    int startGt = (int)Math.Round(startTick * gtPerQuarter / (double)ticksPerQuarter);
                    int endGt = (int)Math.Round(endTick * gtPerQuarter / (double)ticksPerQuarter);

                    double? lastPitch = null;
                    string lastSoundName = null;
                    int? lastPan = null;
                    double? lastVolume = null;

                    for (int gt = startGt; gt < endGt; gt += bendResolutionGt)
                    {
                        double tick = gt * (double)ticksPerQuarter / gtPerQuarter;

                        int bendValue = pitchBendTimeline[channel].LastOrDefault(b => b.tick <= tick).value;
                        double bendSemitone = (bendValue - 8192) * (bendRange / 8192.0);
                        double realNote = noteNum + bendSemitone;

                        int panValue = panTimeline[channel].LastOrDefault(p => p.tick <= tick).value;

                        
                        var volumeEntry = volumeTimeline[channel].LastOrDefault(v => v.tick <= tick);
                        int volumeValue = volumeEntry.value;
                        bool isCC7Set = volumeTimeline[channel].Any(v => v.tick > 0 && v.tick <= tick);

                        double volume;
                        if (isCC7Set)
                        {
                            volume = Math.Round(volumeValue / 127.0, 3);
                        }
                        else
                        {
                            volume = Math.Round(velocity / 127.0, 3);
                        }

                        string soundName = null;
                        double pitch = 1.0;
                        double minDiff = double.MaxValue;
                        int bestPriority = int.MaxValue;
                        double minCenterDist = double.MaxValue;
                        for (int idx = 0; idx < ranges.Length; idx++)
                        {
                            var range = ranges[idx];
                            double p = Math.Pow(2.0, (realNote - range.Center) / 12.0);
                            if (p >= 0.5 && p <= 2.0)
                            {
                                double diff = Math.Abs(Math.Log(p, 2));
                                int priority = (range.Name == namingRule.MidName) ? 0 : 1;
                                double centerDist = Math.Abs(range.Center - midCenter);

                                if (
                                    diff < minDiff ||
                                    (Math.Abs(diff - minDiff) < 1e-8 && priority < bestPriority) ||
                                    (Math.Abs(diff - minDiff) < 1e-8 && priority == bestPriority && centerDist < minCenterDist)
                                )
                                {
                                    minDiff = diff;
                                    bestPriority = priority;
                                    minCenterDist = centerDist;
                                    soundName = range.Name;
                                    pitch = Math.Round(p, 7);
                                }
                            }
                        }
                        if (soundName == null) continue;

                        double panNorm = (panValue - 64) / 64.0;
                        double theta = -panNorm * (Math.PI / 2.0);
                        double x = Math.Round(Math.Sin(theta) * 8.0, 4);
                        double y = Math.Round(Math.Cos(theta) * 8.0, 4);
                        if (y < 0) y = 0;
                        string panStr = $"^{x} ^{y} ^";

                        string playsoundSource = comboBox4.Text == "*" ? "record" : comboBox4.Text;
                        string cmd;

                        if (checkBox1.Checked)
                        {
                            if (string.IsNullOrWhiteSpace(comboBox4.Text))
                            {
                                cmd = "stopsound @s";
                            }
                            else if (string.IsNullOrWhiteSpace(comboBox1.Text))
                            {
                                cmd = $"stopsound @s {comboBox4.Text}";
                            }
                            else
                            {
                                cmd = $"stopsound @s {comboBox4.Text} {comboBox1.Text}";
                            }
                        }
                        else
                        {
                            cmd = $"playsound {soundName} {playsoundSource} @s {panStr} {volume} {pitch}";
                        }

                        if (lastPitch == null || Math.Abs(pitch - lastPitch.Value) > 1e-6 ||
                            soundName != lastSoundName || panValue != lastPan || Math.Abs(volume - (lastVolume ?? -1)) > 1e-3)
                        {
                            string filePath = Path.Combine(noteDir, $"{gt}.mcfunction");
                            List<string> allLines = new List<string>();
                            if (File.Exists(filePath))
                                allLines.AddRange(File.ReadAllLines(filePath, utf8NoBom));
                            allLines.Add(cmd);
                            File.WriteAllLines(filePath, allLines, utf8NoBom);

                            if (!timingDict.ContainsKey(gt))
                                timingDict[gt] = new List<string>();
                            timingDict[gt].Add(cmd);

                            lastPitch = pitch;
                            lastSoundName = soundName;
                            lastPan = panValue;
                            lastVolume = volume;

                            outputCount++;
                        }
                    }
                }
            }

            var noteFiles = Directory.GetFiles(noteDir, "*.mcfunction");
            var allTicks = noteFiles
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Select(name => int.TryParse(name, out var num) ? num : -1)
                .Where(num => num >= 0)
                .ToList();

            int minTick = 0, maxTick = 0;
            if (allTicks.Count > 0)
            {
                allTicks.Sort();
                minTick = allTicks[0];
                maxTick = allTicks[allTicks.Count - 1];

                GenerateBranchFunction(treeDir, noteDir, allTicks, minTick, maxTick, scoreName, namespaceName);
            }

            string mcTagDir = Path.Combine(dataDir, "minecraft", "tags", tagDirName);
            Directory.CreateDirectory(mcTagDir);
            string tickJsonPath = Path.Combine(mcTagDir, "tick.json");
            File.WriteAllText(tickJsonPath,
                "{\n  \"values\": [\n    \"" + namespaceName + ":tick\"\n  ]\n}", utf8NoBom);

            string loadJsonPath = Path.Combine(mcTagDir, "load.json");
            File.WriteAllText(loadJsonPath,
                "{\n  \"values\": [\n    \"" + namespaceName + ":load\"\n  ]\n}", utf8NoBom);

            GenerateUtilityFunctions(funcDir, namespaceName, scoreName, tagName, minTick, maxTick);

            MessageBox.Show(
                $"データパックの生成が完了しました。\n（出力されたノート数: {outputCount}）",
                "完了",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }

        private void GenerateBranchFunction(
            string treeDir,
            string noteDir,
            List<int> tickList,
            int start,
            int end,
            string scoreName,
            string namespaceName)
        {
            string branchName = $"{start}_{end}";
            string branchPath = Path.Combine(treeDir, $"{branchName}.mcfunction");

            var ticksInRange = tickList.FindAll(t => t >= start && t <= end);
            if (ticksInRange.Count == 1)
            {
                int tick = ticksInRange[0];
                string relPath = $"{namespaceName}:note/{tick}";
                string line = $"execute as @s[scores={{{scoreName}={tick}}}] run function {relPath}";
                File.WriteAllText(branchPath, line + "\n", utf8NoBom);
                return;
            }
            int mid = (start + end) / 2;
            var leftTicks = tickList.FindAll(t => t >= start && t <= mid);
            var rightTicks = tickList.FindAll(t => t > mid && t <= end);

            var lines = new List<string>();
            if (leftTicks.Count > 0)
            {
                string leftName = $"{start}_{mid}";
                lines.Add($"execute as @s[scores={{{scoreName}={start}..{mid}}}] run function {namespaceName}:tree/{leftName}");
                GenerateBranchFunction(treeDir, noteDir, tickList, start, mid, scoreName, namespaceName);
            }
            if (rightTicks.Count > 0)
            {
                string rightName = $"{mid + 1}_{end}";
                lines.Add($"execute as @s[scores={{{scoreName}={mid + 1}..{end}}}] run function {namespaceName}:tree/{rightName}");
                GenerateBranchFunction(treeDir, noteDir, tickList, mid + 1, end, scoreName, namespaceName);
            }
            File.WriteAllLines(branchPath, lines, utf8NoBom);
        }

        private void GenerateUtilityFunctions(
            string funcDir,
            string namespaceName,
            string scoreName,
            string tagName,
            int minTick,
            int maxTick)
        {
            string scoreNameCal = scoreName + "_cal";
            string datapackFolder = $"{namespaceName}_midi_datapack";
            string datapackPath = $"file/{datapackFolder}";

            File.WriteAllText(Path.Combine(funcDir, "load.mcfunction"),
                $"scoreboard objectives add {scoreName} dummy\n" +
                $"scoreboard objectives add {scoreNameCal} dummy\n",
                utf8NoBom
            );

            File.WriteAllText(Path.Combine(funcDir, "play.mcfunction"),
                $"execute unless score @s {scoreName} matches 0.. run scoreboard players set @s {scoreName} -1\n" +
                $"execute as @a[scores={{{scoreName}={maxTick + 2}}}] run scoreboard players set @s {scoreName} -1\n" +
                $"tag @s add {tagName}\n" +
                $"scoreboard players set @s {scoreNameCal} 1\n",
                utf8NoBom);

            File.WriteAllText(Path.Combine(funcDir, "pause.mcfunction"),
                $"tag @s remove {tagName}\n" +
                $"scoreboard players set @s {scoreNameCal} 0\n",
                utf8NoBom);

            File.WriteAllText(Path.Combine(funcDir, "stop.mcfunction"),
                $"tag @s remove {tagName}\n" +
                $"scoreboard players set @s {scoreName} -1\n" +
                $"scoreboard players reset {scoreNameCal}\n",
                utf8NoBom);

            File.WriteAllText(Path.Combine(funcDir, "tick.mcfunction"),
                $"execute as @a[scores={{{scoreName}=-1..{maxTick + 1}}}] run scoreboard players operation @s {scoreName} += @s {scoreNameCal}\n" +
                $"execute as @a[tag={tagName}] at @s run function {namespaceName}:tree/{minTick}_{maxTick}\n",
                utf8NoBom);

            File.WriteAllText(Path.Combine(funcDir, "reverse-play.mcfunction"),
                $"tag @s add {tagName}\n" +
                $"scoreboard players remove @s[scores={{{scoreName}={maxTick + 2}}}] {scoreName} 1\n" +
                $"scoreboard players set @s {scoreNameCal} -1\n",
                utf8NoBom);

            File.WriteAllText(Path.Combine(funcDir, "uninstall.mcfunction"),
                $"tag @e remove {tagName}\n" +
                $"scoreboard objectives remove {scoreName}\n" +
                $"scoreboard objectives remove {scoreNameCal}\n" +
                $"datapack disable \"{datapackPath}\"\n" +
                $"tellraw @a [{{\"text\":\"{datapackFolder} をアンインストールしました。必要に応じて、データパックのフォルダーを手動で削除してください。\",\"color\":\"yellow\"}}]\n",
                utf8NoBom
            );
        }
    }
}
