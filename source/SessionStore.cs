using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace ExperimentUI
{
    // One directory per run; no existing participant/session files are reused.
    public sealed class SessionStore : IDisposable
    {
        public string Folder, SessionId;
        private readonly string participant;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        private readonly Dictionary<string, object> metadata = new Dictionary<string, object>();
        private readonly int totalTrials;
        private StreamWriter responses, phases, events;
        private FileStream responseStream;
        private bool disposed, finished;

        public SessionStore(Dataset dataset, string participant, string outputRoot, object display, object layout, bool isTest, string uiMode = "debug", bool isPractice = false, int breakEvery = 100)
        {
            if (dataset == null) throw new ArgumentNullException("dataset");
            if (String.IsNullOrWhiteSpace(outputRoot)) throw new ArgumentException("保存先フォルダが必要です。", "outputRoot");
            if (breakEvery <= 0) throw new ArgumentOutOfRangeException("breakEvery");
            this.participant = participant ?? "";
            totalTrials = dataset.Trials.Count;
            string breakSchedule = isPractice ? "once_at_midpoint" : "every_n_completed_trials";
            int midpoint = BreakSchedule.Midpoint(totalTrials);
            int? periodicBreak = isPractice ? (int?)null : breakEvery;
            int? midpointBreak = isPractice && midpoint > 0 ? (int?)midpoint : null;
            string root = Path.GetFullPath(outputRoot);
            Directory.CreateDirectory(root);
            string safeParticipant = SafeParticipant(this.participant);
            do
            {
                SessionId = Guid.NewGuid().ToString("N");
                Folder = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_" + safeParticipant + "_" + SessionId);
            } while (Directory.Exists(Folder) || File.Exists(Folder));
            Directory.CreateDirectory(Folder);

            try
            {
                // CreateNew prevents accidental reuse even if an unexpected path collision occurs.
                responseStream = new FileStream(Path.Combine(Folder, "responses.csv"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                responses = new StreamWriter(responseStream, new UTF8Encoding(true));
                Csv.WriteRecord(responses,
                    "session_id", "participant", "trial_number", "presentation_order", "attempt", "status", "reason",
                    "A_filename", "A_name", "A_sha256", "B_filename", "B_name", "B_sha256", "C_filename", "C_name", "C_sha256",
                    "selected_position", "selected_filename", "selected_name", "rt_ms",
                    "stimulus_onset_utc", "response_utc", "onset_stopwatch_ticks", "response_stopwatch_ticks", "pointer_x", "pointer_y");
                responseStream.Flush(true);
                phases = NewWriter("phase_timing.csv");
                Csv.WriteRecord(phases, "session_id", "phase", "trial_number", "attempt", "requested_ms", "actual_ms",
                    "onset_utc", "end_utc", "onset_stopwatch_ticks", "end_stopwatch_ticks", "reason");
                events = NewWriter("events.jsonl", false);

                metadata["schema_version"] = 1;
                metadata["application"] = "CVPR2027 Experiment UI";
                metadata["session_id"] = SessionId;
                metadata["participant"] = this.participant;
                metadata["status"] = "running";
                metadata["created_utc"] = UtcText(DateTime.UtcNow);
                metadata["output_folder"] = Folder;
                metadata["dataset"] = dataset;
                metadata["display"] = display;
                metadata["layout"] = layout;
                metadata["is_test"] = isTest || isPractice;
                metadata["ui_mode"] = uiMode;
                metadata["experiment_mode"] = isPractice ? "test" : "production";
                metadata["breaks"] = new Dictionary<string, object>
                {
                    { "schedule", breakSchedule },
                    { "every_completed_trials", periodicBreak },
                    { "after_completed_trials", midpointBreak },
                    { "self_paced", true },
                    { "resume_fixation_ms", 1000 }
                };
                metadata["total_trials"] = totalTrials;
                metadata["completed_count"] = 0;
                metadata["timing"] = new Dictionary<string, object>
                {
                    { "stopwatch_frequency_hz", Stopwatch.Frequency },
                    { "stopwatch_is_high_resolution", Stopwatch.IsHighResolution },
                    { "fixation_requested_ms", 1000 },
                    { "feedback_requested_ms", 500 },
                    { "response_event", "mouse_down" },
                    { "rt_definition", "(response_stopwatch_ticks - onset_stopwatch_ticks) * 1000 / stopwatch_frequency_hz" },
                    { "utc_response_definition", "stimulus_onset_utc + elapsed stopwatch duration" }
                };
                metadata["presentation"] = new Dictionary<string, object>
                {
                    { "shuffle", false },
                    { "trial_order", "ascending numeric trial_number" },
                    { "positions", new string[] { "A:left", "B:center", "C:right" } }
                };
                WriteDisplaySnapshot();
                WriteMetadata();
                Event("session_started", new
                {
                    total_trials = totalTrials, is_test = isTest || isPractice,
                    experiment_mode = isPractice ? "test" : "production", break_schedule = breakSchedule,
                    break_every_completed_trials = periodicBreak, break_after_completed_trials = midpointBreak
                });
            }
            catch
            {
                CloseWriters(false);
                throw;
            }
        }

        public void Response(Trial trial, int presentationOrder, int attempt, int selectedIndex, long onsetTicks,
            DateTime onsetUtc, long responseTicks, int pointerX, int pointerY, string status, string reason)
        {
            EnsureWritable();
            if (trial == null || trial.Images == null || trial.Images.Length != 3)
                throw new ArgumentException("A/B/C の3画像を持つ試行が必要です。", "trial");
            if (selectedIndex < -1 || selectedIndex > 2) throw new ArgumentOutOfRangeException("selectedIndex");
            if (responseTicks < onsetTicks) throw new ArgumentOutOfRangeException("responseTicks", "応答時刻は提示時刻以降にしてください。");
            if (selectedIndex == -1 && !String.Equals(status, "interrupted", StringComparison.Ordinal))
                throw new ArgumentException("選択なしの行は status=interrupted にしてください。", "status");
            if (selectedIndex >= 0 && String.Equals(status, "interrupted", StringComparison.Ordinal))
                throw new ArgumentException("中断行には選択を指定できません。", "selectedIndex");
            double milliseconds = ElapsedMs(onsetTicks, responseTicks);
            Stimulus chosen = selectedIndex < 0 ? null : trial.Images[selectedIndex];
            DateTime utc = AsUtc(onsetUtc);
            object reactionTime = chosen == null ? (object)"" : milliseconds;
            DateTime responseUtc = AddStopwatchDuration(utc, onsetTicks, responseTicks);
            Csv.WriteRecord(responses, SessionId, participant, trial.Number, presentationOrder, attempt, status, reason ?? "",
                trial.Images[0].FileName, trial.Images[0].Name, trial.Images[0].Sha256,
                trial.Images[1].FileName, trial.Images[1].Name, trial.Images[1].Sha256,
                trial.Images[2].FileName, trial.Images[2].Name, trial.Images[2].Sha256,
                chosen == null ? "" : chosen.Position, chosen == null ? "" : chosen.FileName, chosen == null ? "" : chosen.Name,
                reactionTime, UtcText(utc), UtcText(responseUtc), onsetTicks, responseTicks, pointerX, pointerY);
            // Flush the OS buffer after each response, not only at session completion.
            responseStream.Flush(true);
            Event("response", new
            {
                trial_number = trial.Number, presentation_order = presentationOrder, attempt = attempt,
                status = status, reason = reason ?? "", selected_position = chosen == null ? null : chosen.Position,
                selected_filename = chosen == null ? null : chosen.FileName, selected_name = chosen == null ? null : chosen.Name,
                rt_ms = chosen == null ? (double?)null : milliseconds,
                stimulus_onset_utc = UtcText(utc), response_utc = UtcText(responseUtc),
                onset_stopwatch_ticks = onsetTicks, response_stopwatch_ticks = responseTicks,
                pointer_x = pointerX, pointer_y = pointerY
            });
        }

        public void Phase(string phase, int trialNumber, int attempt, long onsetTicks, long endTicks, DateTime onsetUtc, string reason)
        {
            EnsureWritable();
            if (endTicks < onsetTicks) throw new ArgumentOutOfRangeException("endTicks");
            double actual = ElapsedMs(onsetTicks, endTicks);
            object requested = String.Equals(phase, "fixation", StringComparison.OrdinalIgnoreCase) ? (object)1000 :
                String.Equals(phase, "feedback", StringComparison.OrdinalIgnoreCase) ? (object)500 : "";
            DateTime utc = AsUtc(onsetUtc);
            Csv.WriteRecord(phases, SessionId, phase, trialNumber, attempt, requested, actual, UtcText(utc),
                UtcText(AddStopwatchDuration(utc, onsetTicks, endTicks)), onsetTicks, endTicks, reason ?? "");
        }

        public void Event(string name, object details)
        {
            EnsureWritable();
            Dictionary<string, object> record = new Dictionary<string, object>
            {
                { "session_id", SessionId }, { "event", name }, { "utc", UtcText(DateTime.UtcNow) },
                { "stopwatch_ticks", Stopwatch.GetTimestamp() }, { "details", details }
            };
            events.WriteLine(json.Serialize(record));
            events.Flush();
        }

        public void Finish(string status, int completedCount)
        {
            if (disposed) throw new ObjectDisposedException("SessionStore");
            if (finished) return;
            if (status != "completed" && status != "aborted" && status != "error")
                throw new ArgumentException("終了状態は completed / aborted / error のいずれかです。", "status");
            if (completedCount < 0 || completedCount > totalTrials) throw new ArgumentOutOfRangeException("completedCount");
            Event("session_finished", new { status = status, completed_count = completedCount });
            metadata["status"] = status;
            metadata["completed_count"] = completedCount;
            metadata["finished_utc"] = UtcText(DateTime.UtcNow);
            WriteMetadata();
            finished = true;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            CloseWriters(true);
        }

        private StreamWriter NewWriter(string fileName, bool byteOrderMark = true)
        {
            return new StreamWriter(new FileStream(Path.Combine(Folder, fileName), FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(byteOrderMark));
        }

        private void WriteDisplaySnapshot()
        {
            Dictionary<string, object> snapshot = new Dictionary<string, object>
            {
                { "session_id", SessionId },
                { "participant_id", participant },
                { "ui_mode", metadata["ui_mode"] },
                { "experiment_mode", metadata["experiment_mode"] },
                { "display", metadata["display"] },
                { "created_utc", metadata["created_utc"] }
            };
            byte[] bytes = new UTF8Encoding(false).GetBytes(json.Serialize(snapshot));
            using (FileStream stream = new FileStream(Path.Combine(Folder, "display.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private void WriteMetadata()
        {
            string target = Path.Combine(Folder, "session.json");
            string temporary = Path.Combine(Folder, ".session-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(json.Serialize(metadata));
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
        }

        private void EnsureWritable()
        {
            if (disposed) throw new ObjectDisposedException("SessionStore");
            if (finished) throw new InvalidOperationException("この実験セッションは終了しています。");
        }

        private void CloseWriters(bool throwOnError)
        {
            Exception first = null;
            foreach (StreamWriter writer in new StreamWriter[] { responses, phases, events })
            {
                if (writer == null) continue;
                try { writer.Dispose(); }
                catch (Exception ex) { if (first == null) first = ex; }
            }
            // Also closes the raw stream when its StreamWriter was not constructed.
            if (responseStream != null)
            {
                try { responseStream.Dispose(); }
                catch (Exception ex) { if (first == null) first = ex; }
            }
            if (throwOnError && first != null) throw new IOException("実験ログを閉じる際に保存エラーが発生しました。", first);
        }

        private static double ElapsedMs(long onset, long end)
        {
            return ((double)(end - onset)) * 1000.0 / Stopwatch.Frequency;
        }

        private static DateTime AsUtc(DateTime value)
        {
            return value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
        }

        private static DateTime AddStopwatchDuration(DateTime utc, long onset, long end)
        {
            double dateTicks = ((double)(end - onset)) * TimeSpan.TicksPerSecond / Stopwatch.Frequency;
            return utc.AddTicks((long)Math.Round(dateTicks, MidpointRounding.ToEven));
        }

        private static string UtcText(DateTime value)
        {
            return AsUtc(value).ToString("o", CultureInfo.InvariantCulture);
        }

        private static string SafeParticipant(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            StringBuilder result = new StringBuilder();
            foreach (char character in value)
            {
                if (result.Length >= 20) break;
                result.Append(Array.IndexOf(invalid, character) >= 0 || Char.IsControl(character) ? '_' : character);
            }
            string safe = result.ToString().Trim().TrimEnd('.', ' ');
            return safe.Length == 0 ? "anonymous" : safe;
        }
    }
}
