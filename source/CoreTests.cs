using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using ExperimentUI;

internal static class CoreTests
{
    private static int passed;
    private static string root;

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static string Folder(string name)
    {
        string path = Path.Combine(root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Png(string folder, string name, int w, int h)
    {
        using (Bitmap bitmap = new Bitmap(w, h))
        {
            using (Graphics g = Graphics.FromImage(bitmap)) g.Clear(Color.CornflowerBlue);
            bitmap.Save(Path.Combine(folder, name), ImageFormat.Png);
        }
    }

    private static void TrialImages(string folder, string number, int size)
    {
        foreach (string position in new string[] { "A", "B", "C" })
            Png(folder, number + "_" + position + "_対象_部品.png", size, size);
    }

    private static void Reject(string folder, string name)
    {
        bool rejected = false;
        try { Dataset.Load(folder); }
        catch (InvalidDataException) { rejected = true; }
        Assert(rejected, name);
    }

    public static int Main()
    {
        root = Path.Combine(Path.GetTempPath(), "ExperimentUI-CoreTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string valid = Folder("valid");
            TrialImages(valid, "10", 1024);
            TrialImages(valid, "2", 1024);
            TrialImages(valid, "1", 1024);
            File.WriteAllText(Path.Combine(valid, "README.txt"), "ignored");
            Dataset d = Dataset.Load(valid);
            Assert(d.Trials.Count == 3 && d.Trials[0].Number == 1 && d.Trials[1].Number == 2 && d.Trials[2].Number == 10, "numeric sorting, nonPNG ignored");
            Assert(d.Trials[0].Images[0].Position == "A" && d.Trials[0].Images[1].Position == "B" && d.Trials[0].Images[2].Position == "C", "ABC order");
            Assert(d.Trials[0].Images[0].Name == "対象_部品", "Unicode names and underscores retained");
            Assert(d.Trials[0].Images[0].Sha256.Length == 64, "SHA256 generated");
            Assert(d.Warnings.Count == 1, "gaps warned, recommended image size accepted");
            using (FileStream lockTest = new FileStream(d.Trials[0].Images[0].FullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert(lockTest.Length > 0, "image handles released");

            string missing = Folder("missing");
            Png(missing, "1_A_test.png", 16, 16);
            Png(missing, "1_B_test.png", 16, 16);
            Reject(missing, "missing C rejected");

            string duplicate = Folder("duplicate");
            TrialImages(duplicate, "1", 16);
            Png(duplicate, "01_A_duplicate.png", 16, 16);
            Reject(duplicate, "leading zero duplicate position rejected");

            string corrupt = Folder("corrupt");
            TrialImages(corrupt, "1", 16);
            File.WriteAllText(Path.Combine(corrupt, "1_B_対象_部品.png"), "not PNG");
            Reject(corrupt, "corrupt PNG rejected");

            string rectangular = Folder("rectangular");
            TrialImages(rectangular, "1", 16);
            Png(rectangular, "1_A_対象_部品.png", 24, 16);
            Reject(rectangular, "nonsquare image rejected");

            string malformed = Folder("malformed");
            TrialImages(malformed, "1", 16);
            Png(malformed, "bad_name.png", 16, 16);
            Reject(malformed, "malformed PNG filename rejected");

            foreach (string badNumber in new string[] { "0", "-1", "2147483648" })
            {
                string badFolder = Folder("number" + badNumber);
                TrialImages(badFolder, badNumber, 16);
                Reject(badFolder, "invalid trial number " + badNumber + " rejected");
            }

            string small = Folder("small");
            TrialImages(small, "1", 16);
            Assert(Dataset.Load(small).Warnings.Count == 3, "square non1024 images accepted with warnings");
            string caseFolder = Folder("case");
            Png(caseFolder, "001_a_case.PNG", 16, 16);
            Png(caseFolder, "001_b_case.PnG", 16, 16);
            Png(caseFolder, "001_c_case.png", 16, 16);
            Assert(Dataset.Load(caseFolder).Trials[0].Number == 1, "case insensitive extension and positions");
            Reject(Folder("empty"), "empty folder rejected");
            Reject(Path.Combine(root, "nonexistent"), "nonexistent folder rejected");
            string disguised = Folder("disguised");
            TrialImages(disguised, "1", 16);
            using (Bitmap jpeg = new Bitmap(16, 16)) jpeg.Save(Path.Combine(disguised, "1_A_対象_部品.png"), ImageFormat.Jpeg);
            Reject(disguised, "JPEG disguised as PNG rejected");

            Assert(Csv.Escape("a,\"b\"\r\nc") == "\"a,\"\"b\"\"\r\nc\"", "CSV commas, quotes, CRLF escaped");
            Assert(Csv.Escape("=SUM(1)") == "'=SUM(1)" && Csv.Escape("  +1") == "'  +1" && Csv.Escape("\tcmd") == "'\tcmd", "spreadsheet formula injection escaped");
            Assert(Csv.Escape(-1.25) == "-1.25", "negative numeric data retained");
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Assert(Csv.Escape(1.25) == "1.25", "CSV numeric formatting independent of locale");
            }
            finally { CultureInfo.CurrentCulture = previous; }
            string csvPath = Path.Combine(root, "records.csv");
            using (StreamWriter writer = new StreamWriter(csvPath, false, new UTF8Encoding(true)))
            {
                Csv.WriteRecord(writer, "trial", "choice", "rt_ms");
                Csv.WriteRecord(writer, 1, "B", 123.45);
                using (FileStream f = new FileStream(csvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    Assert(f.Length > 0, "CSV records flushed before writer closes");
            }
            Console.WriteLine("ALL " + passed + " CHECKS PASSED");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            // This root was created above from a GUID under the temp directory.
            string fullRoot = Path.GetFullPath(root);
            string tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (fullRoot.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(fullRoot).StartsWith("ExperimentUI-CoreTests-", StringComparison.Ordinal))
                Directory.Delete(fullRoot, true);
        }
    }
}
