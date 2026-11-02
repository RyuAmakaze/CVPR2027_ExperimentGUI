using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ExperimentUI
{
    public class Stimulus
    {
        public string Position, Name, FileName, FullPath, Sha256;
        public int Width, Height;
    }

    public class Trial
    {
        public int Number;
        public Stimulus[] Images;
    }

    public class Dataset
    {
        public string Folder;
        public List<Trial> Trials = new List<Trial>();
        public List<string> Warnings = new List<string>();

        // Trial numbers are numeric. A/B/C always map to left/center/right.
        private static readonly Regex FilePattern = new Regex(
            @"^([0-9]+)_([ABC])_(.+)\.png$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static Dataset Load(string folder)
        {
            if (String.IsNullOrWhiteSpace(folder))
                throw new InvalidDataException("実験データフォルダを選択してください。");

            Dataset result = new Dataset();
            try { result.Folder = Path.GetFullPath(folder); }
            catch (Exception ex)
            {
                throw new InvalidDataException("データフォルダのパスが無効です。\r\n" + ex.Message, ex);
            }
            if (!Directory.Exists(result.Folder))
                throw new InvalidDataException("データフォルダが存在しないか、アクセスできません。\r\n" + result.Folder);

            string[] files;
            try { files = Directory.GetFiles(result.Folder); }
            catch (Exception ex)
            {
                throw new InvalidDataException("データフォルダを読み込めません。\r\n" + ex.Message, ex);
            }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            SortedDictionary<int, Trial> trials = new SortedDictionary<int, Trial>();
            List<string> errors = new List<string>();
            int pngCount = 0;

            foreach (string path in files)
            {
                if (!String.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                    continue;
                pngCount++;
                string fileName = Path.GetFileName(path);
                Match match = FilePattern.Match(fileName);
                if (!match.Success)
                {
                    errors.Add(fileName + " : ファイル名は {正の試行番号}_{A/B/C}_{Name}.png にしてください。");
                    continue;
                }
                int number;
                if (!Int32.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out number) || number < 1)
                {
                    errors.Add(fileName + " : 試行番号は 1 ～ 2147483647 の整数にしてください。");
                    continue;
                }
                string position = match.Groups[2].Value.ToUpperInvariant();
                int index = position[0] - 'A';
                Trial trial;
                if (!trials.TryGetValue(number, out trial))
                {
                    trial = new Trial { Number = number, Images = new Stimulus[3] };
                    trials.Add(number, trial);
                }
                if (trial.Images[index] != null)
                {
                    errors.Add(fileName + " : 試行 " + number + " の " + position + " が重複しています（" + trial.Images[index].FileName + "）。");
                    continue;
                }

                Stimulus stimulus = new Stimulus
                {
                    Position = position,
                    Name = match.Groups[3].Value,
                    FileName = fileName,
                    FullPath = path
                };
                try
                {
                    // Decode the entire image at validation time, then release all file handles.
                    using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        using (SHA256 sha = SHA256.Create())
                            stimulus.Sha256 = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                        stream.Position = 0;
                        using (Image image = Image.FromStream(stream, false, true))
                        {
                            if (image.RawFormat.Guid != ImageFormat.Png.Guid)
                                throw new InvalidDataException("拡張子は PNG ですが、画像の実形式が PNG ではありません。");
                            using (Bitmap decoded = new Bitmap(image))
                            {
                                stimulus.Width = decoded.Width;
                                stimulus.Height = decoded.Height;
                            }
                        }
                    }
                    if (stimulus.Width != stimulus.Height)
                        throw new InvalidDataException("正方形の画像が必要です（" + stimulus.Width + "×" + stimulus.Height + "）。");
                    if (stimulus.Width != 1024)
                        result.Warnings.Add(fileName + " : " + stimulus.Width + "×" + stimulus.Height + " px（推奨は 1024×1024 px）。");
                    trial.Images[index] = stimulus;
                }
                catch (Exception ex)
                {
                    errors.Add(fileName + " : 画像を読み込めません。" + ex.Message);
                }
            }

            if (pngCount == 0)
                errors.Add("フォルダ直下に PNG 画像がありません。");
            foreach (KeyValuePair<int, Trial> pair in trials)
            {
                List<string> missing = new List<string>();
                for (int i = 0; i < 3; i++)
                    if (pair.Value.Images[i] == null) missing.Add(((char)('A' + i)).ToString());
                if (missing.Count > 0)
                    errors.Add("試行 " + pair.Key + " : " + String.Join(" / ", missing.ToArray()) + " の有効な画像がありません。");
                result.Trials.Add(pair.Value);
            }
            if (errors.Count > 0)
            {
                int shown = Math.Min(errors.Count, 25);
                string message = "実験データを確認してください。開始できません。\r\n\r\n" + String.Join("\r\n", errors.GetRange(0, shown).ToArray());
                if (errors.Count > shown) message += "\r\nほか " + (errors.Count - shown) + " 件のエラーがあります。";
                throw new InvalidDataException(message);
            }

            int previous = 0;
            bool hasGap = false;
            foreach (Trial trial in result.Trials)
            {
                if ((long)trial.Number != (long)previous + 1) hasGap = true;
                previous = trial.Number;
            }
            if (hasGap)
                result.Warnings.Add("試行番号に欠番があります。存在する試行を番号の小さい順に提示します。番号の振り直しはしません。");
            return result;
        }
    }

    public static class Csv
    {
        // Protect untrusted text when opening CSV in spreadsheet software. Exact source
        // strings are retained by the caller's JSON metadata as the authoritative copy.
        public static string Escape(object value)
        {
            string text;
            if (value == null) text = "";
            else if (value is string)
            {
                text = (string)value;
                int i = 0;
                while (i < text.Length && Char.IsWhiteSpace(text[i])) i++;
                bool controlPrefix = text.Length > 0 && (text[0] == '\t' || text[0] == '\r' || text[0] == '\n');
                if (controlPrefix || (i < text.Length && (text[i] == '=' || text[i] == '+' || text[i] == '-' || text[i] == '@')))
                    text = "'" + text;
            }
            else if (value is DateTime) text = ((DateTime)value).ToString("o", CultureInfo.InvariantCulture);
            else
            {
                IFormattable formattable = value as IFormattable;
                text = formattable == null ? value.ToString() : formattable.ToString(null, CultureInfo.InvariantCulture);
            }
            if (text.IndexOfAny(new char[] { ',', '"', '\r', '\n' }) >= 0)
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            return text;
        }

        public static void WriteRecord(StreamWriter writer, params object[] values)
        {
            if (writer == null) throw new ArgumentNullException("writer");
            if (values == null) throw new ArgumentNullException("values");
            string[] fields = new string[values.Length];
            for (int i = 0; i < values.Length; i++) fields[i] = Escape(values[i]);
            writer.WriteLine(String.Join(",", fields));
            writer.Flush();
        }
    }
}
