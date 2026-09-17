using System.Text;

namespace ArcheCore.LoadTester
{
    /// <summary>
    /// Every Console.WriteLine already in Program.cs goes to both places
    /// automatically once this is installed as Console.Out — nothing else
    /// in the codebase needs to change or know logging exists.
    ///
    /// This exists specifically because copy-pasting console output by
    /// hand is error-prone: it's easy to grab an old terminal buffer, or a
    /// scrollback that got truncated, and not notice the timestamps don't
    /// match the run you meant to share. A file on disk with the run's
    /// start time in its own name removes that failure mode entirely -
    /// there's no "which one was I looking at" ambiguity once the
    /// filename says exactly when the run started.
    /// </summary>
    public sealed class TeeTextWriter : TextWriter
    {
        private readonly TextWriter _console;
        private readonly StreamWriter _file;

        public override Encoding Encoding => _console.Encoding;

        public TeeTextWriter(TextWriter console, string logFilePath)
        {
            _console = console;

            // AutoFlush so a crash or Ctrl+C never loses the last few
            // lines sitting in a buffer — exactly the lines you'd want
            // most when something goes wrong.
            _file = new StreamWriter(logFilePath, append: false) { AutoFlush = true };
        }

        public override void Write(char value)
        {
            _console.Write(value);
            _file.Write(value);
        }

        public override void Write(string? value)
        {
            _console.Write(value);
            _file.Write(value);
        }

        public override void WriteLine(string? value)
        {
            _console.WriteLine(value);
            _file.WriteLine(value);
        }

        public override void Flush()
        {
            _console.Flush();
            _file.Flush();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _file.Dispose();
            base.Dispose(disposing);
        }
    }
}