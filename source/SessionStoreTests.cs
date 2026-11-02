using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using ExperimentUI;

internal static class SessionStoreTests
{
    private static int passed;
    private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();

    private static void Assert(bool condition, string description)
    {
        if (!condition) throw new Exception("FAIL: " + description);
        passed++;
        Console.WriteLine("PASS: " + description);
    }

    private static Dataset TestData(int trialCount = 1)
    {
        Dataset d = new Dataset { Folder = "C:\\test\\データ" };
        for (int trialIndex = 0; trialIndex < trialCount; trialIndex++)
        {
            Trial t = new Trial { Number = 7 + trialIndex * 10, Images = new Stimulus[3] };
            for (int i = 0; i < 3; i++)
                t.Images[i] = new Stimulus { Position = ((char)('A' + i)).ToString(), Name = i == 1 ? "=対象,\"部品\"" : "通常", FileName = t.Number + "_" + (char)('A' + i) + "_対象,部品.png", FullPath = "C:\\test\\source_" + trialIndex + "_" + i + ".png", Sha256 = new string((char)('a' + i), 64), Width = 1024, Height = 1024 };
            d.Trials.Add(t);
        }
        return d;
    }

    // Small RFC4180 parser independent of production Csv.Escape.
    private static List<string[]> ReadCsv(string path)
    {
        string text;
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            text = reader.ReadToEnd();
        List<string[]> rows = new List<string[]>();
        List<string> row = new List<string>();
        StringBuilder field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (!quoted && c == ',') { row.Add(field.ToString()); field.Length = 0; }
            else if (!quoted && (c == '\n' || c == '\r'))
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Length = 0;
                rows.Add(row.ToArray()); row.Clear();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }

    private static string Field(List<string[]> rows, int row, string header)
    {
        int index = Array.IndexOf(rows[0], header);
        if (index < 0) throw new Exception("Missing CSV column: " + header);
        return rows[row][index];
    }

    public static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "ExperimentUI-SessionTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Dataset dataset = TestData();
            string folder, id;
            DateTime onset = new DateTime(2026, 11, 2, 1, 2, 3, DateTimeKind.Utc);
            long ticks = Stopwatch.Frequency * 10;
            using (SessionStore store = new SessionStore(dataset, "被験者:/?=verylongparticipantname", root, new { width_px = 1920, height_px = 1080 }, new { image_size_px = 540 }, true))
            {
                folder = store.Folder; id = store.SessionId;
                Dictionary<string, object> initial = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(folder, "session.json")));
                Assert((string)initial["status"] == "running", "initial metadata status is running");
                string displayPath = Path.Combine(folder, "display.json");
                Dictionary<string, object> displaySnapshot = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(displayPath));
                Assert((string)displaySnapshot["session_id"] == id && (string)displaySnapshot["participant_id"] == "被験者:/?=verylongparticipantname", "display saved automatically at startup with exact session and participant IDs");
                Assert((string)displaySnapshot["ui_mode"] == (string)initial["ui_mode"] && (string)displaySnapshot["experiment_mode"] == "production", "display snapshot records matching UI and experiment modes");
                Assert(Json.Serialize(displaySnapshot["display"]) == Json.Serialize(initial["display"]), "display.json and session.json contain identical display snapshots");
                Assert((string)displaySnapshot["created_utc"] == (string)initial["created_utc"], "automatic display capture uses the session creation UTC");
                byte[] displayBytes = File.ReadAllBytes(displayPath);
                Assert(displayBytes.Length > 0 && displayBytes[0] == (byte)'{', "display JSON uses UTF8 without BOM");
                Dictionary<string, object> defaultBreaks = (Dictionary<string, object>)initial["breaks"];
                Assert((string)initial["experiment_mode"] == "production" && Convert.ToInt32(defaultBreaks["every_completed_trials"]) == 100, "existing constructor defaults to production and 100 completed trials");
                Assert((string)defaultBreaks["schedule"] == "every_n_completed_trials" && defaultBreaks["after_completed_trials"] == null, "production metadata uses periodic schedule without midpoint");
                Assert((bool)defaultBreaks["self_paced"] && Convert.ToInt32(defaultBreaks["resume_fixation_ms"]) == 1000, "breaks are self paced and resume with 1000ms fixation");
                store.Response(dataset.Trials[0], 1, 1, 1, ticks, onset, ticks + Stopwatch.Frequency / 4, 600, 400, "valid", "");
                store.Response(dataset.Trials[0], 1, 2, -1, ticks, onset, ticks + Stopwatch.Frequency / 2, -1, -1, "interrupted", "focus_lost");
                store.Phase("fixation", 7, 1, ticks, ticks + Stopwatch.Frequency, onset, "");
                store.Phase("feedback", 7, 1, ticks, ticks + Stopwatch.Frequency / 2, onset, "");
                store.Event("日本語イベント", new { exact_name = dataset.Trials[0].Images[1].Name });
                List<string[]> liveRows = ReadCsv(Path.Combine(folder, "responses.csv"));
                Assert(liveRows.Count == 3, "response CSV readable and flushed before disposal");
                Assert(Field(liveRows, 1, "selected_position") == "B" && Field(liveRows, 1, "rt_ms") == "250", "selected B and 250ms RT recorded");
                Assert(Field(liveRows, 2, "selected_position") == "" && Field(liveRows, 2, "rt_ms") == "" && Field(liveRows, 2, "status") == "interrupted", "interrupted trial has no selection or RT");
                Assert(Field(liveRows, 1, "B_filename") == dataset.Trials[0].Images[1].FileName && Field(liveRows, 1, "B_sha256") == dataset.Trials[0].Images[1].Sha256, "ABC filenames and source SHA256 retained");
                Assert(Field(liveRows, 1, "selected_name") == "'" + dataset.Trials[0].Images[1].Name, "CSV formula protection applied to source name");
                DateTime responseUtc = DateTime.Parse(Field(liveRows, 1, "response_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                Assert(responseUtc == onset.AddMilliseconds(250) && responseUtc.Kind == DateTimeKind.Utc, "response UTC consistent with elapsed monotonic ticks");
                Assert(Int64.Parse(Field(liveRows, 1, "response_stopwatch_ticks")) - Int64.Parse(Field(liveRows, 1, "onset_stopwatch_ticks")) == Stopwatch.Frequency / 4, "raw response timestamps retained");
                store.Finish("completed", 1);
            }

            byte[] csvBytes = File.ReadAllBytes(Path.Combine(folder, "responses.csv"));
            Assert(csvBytes.Length > 3 && csvBytes[0] == 239 && csvBytes[1] == 187 && csvBytes[2] == 191, "CSV is UTF8 with BOM");
            Dictionary<string, object> final = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(folder, "session.json")));
            Assert((string)final["status"] == "completed" && Convert.ToInt32(final["completed_count"]) == 1, "completed status persisted atomically");
            Dictionary<string, object> storedDataset = (Dictionary<string, object>)final["dataset"];
            IList storedTrials = (IList)storedDataset["Trials"];
            Dictionary<string, object> storedTrial = (Dictionary<string, object>)storedTrials[0];
            IList storedImages = (IList)storedTrial["Images"];
            Assert((string)((Dictionary<string, object>)storedImages[1])["Name"] == dataset.Trials[0].Images[1].Name, "JSON preserves exact original source strings");
            Dictionary<string, object> timing = (Dictionary<string, object>)final["timing"];
            Assert(Convert.ToInt64(timing["stopwatch_frequency_hz"]) == Stopwatch.Frequency && Convert.ToInt32(timing["fixation_requested_ms"]) == 1000 && Convert.ToInt32(timing["feedback_requested_ms"]) == 500, "clock and requested timing stored");
            Assert((bool)final["is_test"] && ((Dictionary<string, object>)final["display"])["width_px"].ToString() == "1920", "test flag and display snapshot stored");
            Assert(Directory.GetFiles(folder, "*.tmp").Length == 0, "atomic metadata leaves no temporary file");
            List<string[]> phaseRows = ReadCsv(Path.Combine(folder, "phase_timing.csv"));
            Assert(Field(phaseRows, 1, "actual_ms") == "1000" && Field(phaseRows, 1, "requested_ms") == "1000" && Field(phaseRows, 2, "actual_ms") == "500", "actual fixation/feedback durations stored");
            string[] eventLines = File.ReadAllLines(Path.Combine(folder, "events.jsonl"), Encoding.UTF8);
            byte[] eventBytes = File.ReadAllBytes(Path.Combine(folder, "events.jsonl"));
            Assert(eventBytes.Length > 0 && eventBytes[0] == (byte)'{', "JSONL is UTF8 without BOM");
            bool exactEvent = false;
            foreach (string line in eventLines)
            {
                Dictionary<string, object> entry = Json.Deserialize<Dictionary<string, object>>(line);
                Assert((string)entry["session_id"] == id && entry.ContainsKey("utc") && entry.ContainsKey("stopwatch_ticks"), "JSONL event has session and clocks");
                if ((string)entry["event"] == "日本語イベント")
                    exactEvent = (string)((Dictionary<string, object>)entry["details"])["exact_name"] == dataset.Trials[0].Images[1].Name;
            }
            Assert(exactEvent, "JSONL preserves exact original strings");
            string originalCsv = File.ReadAllText(Path.Combine(folder, "responses.csv"));
            string originalDisplay = File.ReadAllText(Path.Combine(folder, "display.json"));
            using (SessionStore another = new SessionStore(dataset, "被験者:/?=verylongparticipantname", root, null, null, false))
            {
                Assert(another.Folder != folder && another.SessionId != id, "second session gets unique directory and ID");
                Dictionary<string, object> anotherDisplay = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(another.Folder, "display.json")));
                Assert((string)anotherDisplay["session_id"] == another.SessionId && anotherDisplay["display"] == null, "each unique session gets its own automatic display file");
                another.Finish("aborted", 0);
                Dictionary<string, object> aborted = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(another.Folder, "session.json")));
                Assert((string)aborted["status"] == "aborted", "aborted status persisted");
            }
            Assert(File.ReadAllText(Path.Combine(folder, "responses.csv")) == originalCsv, "existing session never overwritten");
            Assert(File.ReadAllText(Path.Combine(folder, "display.json")) == originalDisplay && !File.Exists(Path.Combine(root, "display.json")), "display snapshots stay inside their own sessions and never overwrite prior captures");
            using (SessionStore practice = new SessionStore(TestData(8), "practice", root, null, null, false, "experiment", true))
            {
                Dictionary<string, object> practiceMetadata = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(practice.Folder, "session.json")));
                Assert((string)practiceMetadata["experiment_mode"] == "test" && (bool)practiceMetadata["is_test"], "practice executable is test mode and marked is_test without automation");
                Dictionary<string, object> practiceDisplay = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(practice.Folder, "display.json")));
                Assert((string)practiceDisplay["experiment_mode"] == "test" && (string)practiceDisplay["ui_mode"] == "experiment", "test sessions automatically capture the correct display mode");
                Dictionary<string, object> practiceBreaks = (Dictionary<string, object>)practiceMetadata["breaks"];
                Assert((string)practiceBreaks["schedule"] == "once_at_midpoint" && practiceBreaks["every_completed_trials"] == null && Convert.ToInt32(practiceBreaks["after_completed_trials"]) == 4, "eight practice trials schedule one rest after four, independent of gapped source IDs");
                practice.Event("break_started", new { completed_count = 4, onset_stopwatch_ticks = ticks });
                practice.Event("break_resumed", new { completed_count = 4, end_stopwatch_ticks = ticks + Stopwatch.Frequency * 5 });
                practice.Finish("aborted", 0);
                string[] practiceEvents;
                using (FileStream eventStream = new FileStream(Path.Combine(practice.Folder, "events.jsonl"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (StreamReader eventReader = new StreamReader(eventStream, Encoding.UTF8))
                    practiceEvents = eventReader.ReadToEnd().Split(new string[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                bool startFound = false, resumeFound = false;
                foreach (string line in practiceEvents)
                {
                    Dictionary<string, object> entry = Json.Deserialize<Dictionary<string, object>>(line);
                    Dictionary<string, object> details = (Dictionary<string, object>)entry["details"];
                    if ((string)entry["event"] == "session_started")
                    {
                        Assert((bool)details["is_test"] && (string)details["experiment_mode"] == "test", "session_started agrees with practice metadata");
                        Assert((string)details["break_schedule"] == "once_at_midpoint" && details["break_every_completed_trials"] == null && Convert.ToInt32(details["break_after_completed_trials"]) == 4, "session_started mirrors one midpoint break");
                    }
                    if ((string)entry["event"] == "break_started")
                        startFound = Convert.ToInt64(details["onset_stopwatch_ticks"]) == ticks;
                    if ((string)entry["event"] == "break_resumed")
                        resumeFound = Convert.ToInt64(details["end_stopwatch_ticks"]) == ticks + Stopwatch.Frequency * 5;
                }
                Assert(startFound && resumeFound, "break start and resume raw clocks preserved in events");
            }
            foreach (int total in new int[] { 0, 1, 2, 5 })
            {
                using (SessionStore tiny = new SessionStore(TestData(total), "practice-tiny", root, null, null, false, "experiment", true))
                {
                    Dictionary<string, object> tinyMetadata = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(Path.Combine(tiny.Folder, "session.json")));
                    Dictionary<string, object> tinyBreaks = (Dictionary<string, object>)tinyMetadata["breaks"];
                    object after = tinyBreaks["after_completed_trials"];
                    Assert((string)tinyBreaks["schedule"] == "once_at_midpoint" && tinyBreaks["every_completed_trials"] == null && (total < 2 ? after == null : Convert.ToInt32(after) == total / 2), "practice midpoint metadata for total=" + total);
                    tiny.Finish("aborted", 0);
                }
            }
            int directoriesBeforeInvalid = Directory.GetDirectories(root).Length;
            bool invalidBreakRejected = false;
            try { using (new SessionStore(dataset, "invalid", root, null, null, false, "experiment", false, 0)) { } }
            catch (ArgumentOutOfRangeException) { invalidBreakRejected = true; }
            Assert(invalidBreakRejected && Directory.GetDirectories(root).Length == directoriesBeforeInvalid, "invalid break interval rejected before creating output session");
            string invalidRoot = Path.Combine(root, "not-a-directory");
            File.WriteAllText(invalidRoot, "sentinel");
            bool failed = false;
            try { using (new SessionStore(dataset, "test", invalidRoot, null, null, true)) { } }
            catch (IOException) { failed = true; }
            Assert(failed && File.ReadAllText(invalidRoot) == "sentinel", "output IO failure propagates without overwriting file");
            Console.WriteLine("ALL " + passed + " CHECKS PASSED");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            string fullRoot = Path.GetFullPath(root);
            if (fullRoot.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(fullRoot).StartsWith("ExperimentUI-SessionTests-", StringComparison.Ordinal))
                Directory.Delete(fullRoot, true);
        }
    }
}
