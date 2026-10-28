// <copyright>
// Copyright by the Spark Development Network
//
// Licensed under the Rock Community License (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.rockrms.com/license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//

using System.Diagnostics;

namespace RockSampleData.Loader
{
    /// <summary>
    /// One line of a load's progress, as the loader CLI prints it.
    /// </summary>
    public sealed class LoadProgressReport
    {
        internal LoadProgressReport( string message, string stepName, int completedSteps, int totalSteps, LoadTiming timing = null )
        {
            Message = message;
            StepName = stepName;
            CompletedSteps = completedSteps;
            TotalSteps = totalSteps;
            Timing = timing;
        }

        /// <summary>
        /// The line, for example "Giving" or "  Gifts: 2,836 ($612,040) in 380 batches". Detail lines start with spaces.
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// The step that starts with this line, for example "People" or "Giving". Null for a detail line.
        /// </summary>
        public string StepName { get; }

        /// <summary>
        /// How many steps have finished.
        /// </summary>
        public int CompletedSteps { get; }

        /// <summary>
        /// How many steps this load runs, after part selection.
        /// </summary>
        public int TotalSteps { get; }

        /// <summary>
        /// The wall-clock time this line reports, for a line that ends something timed, such as a step. Null for other lines.
        /// </summary>
        public LoadTiming Timing { get; }
    }

    /// <summary>
    /// What a timed piece of a load is.
    /// </summary>
    public enum LoadTimingKind
    {
        /// <summary>
        /// A whole load step, such as "Giving".
        /// </summary>
        Step,

        /// <summary>
        /// A piece of a step, such as "Bulk email" in "Communications".
        /// </summary>
        Part,

        /// <summary>
        /// One bulk copy into a table, with its row count. It includes turning the table's indexes off, but not rebuilding them.
        /// </summary>
        BulkCopy,

        /// <summary>
        /// Rebuilding the nonclustered indexes a large bulk copy turned off.
        /// </summary>
        IndexRebuild,

        /// <summary>
        /// Work outside the steps, such as the commit, or the whole load.
        /// </summary>
        Load
    }

    /// <summary>
    /// The wall-clock time one piece of a load took.
    /// </summary>
    public sealed class LoadTiming
    {
        internal LoadTiming( LoadTimingKind kind, string name, string stepName, long milliseconds, int? rows = null )
        {
            Kind = kind;
            Name = name;
            StepName = stepName;
            Milliseconds = milliseconds;
            Rows = rows;
        }

        /// <summary>
        /// What was timed.
        /// </summary>
        public LoadTimingKind Kind { get; }

        /// <summary>
        /// The step, part, or table, for example "Communications", "Bulk email", or "CommunicationRecipient".
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The step it ran in. Null for <see cref="LoadTimingKind.Load"/>.
        /// </summary>
        public string StepName { get; }

        /// <summary>
        /// The wall-clock milliseconds it took.
        /// </summary>
        public long Milliseconds { get; }

        /// <summary>
        /// The rows a bulk copy wrote. Null for other kinds.
        /// </summary>
        public int? Rows { get; }
    }

    /// <summary>
    /// Where the loader reports its progress. It reports on the loading thread, one line at a time, and
    /// never writes to the console.
    /// </summary>
    internal sealed class LoadLog
    {
        private readonly IProgress<LoadProgressReport> _progress;

        public LoadLog( IProgress<LoadProgressReport> progress )
        {
            _progress = progress;
        }

        /// <summary>
        /// How many steps this load runs.
        /// </summary>
        public int TotalSteps { get; set; }

        /// <summary>
        /// How many steps have finished.
        /// </summary>
        public int CompletedSteps { get; private set; }

        /// <summary>
        /// The step running now. Null before the first step, between steps, and after the last.
        /// </summary>
        public string CurrentStep { get; private set; }

        /// <summary>
        /// Reports a detail line.
        /// </summary>
        public void Write( string message )
        {
            _progress?.Report( new LoadProgressReport( message, null, CompletedSteps, TotalSteps ) );
        }

        /// <summary>
        /// Reports a detail line that carries a timing.
        /// </summary>
        public void WriteTiming( string message, LoadTiming timing )
        {
            _progress?.Report( new LoadProgressReport( message, null, CompletedSteps, TotalSteps, timing ) );
        }

        /// <summary>
        /// Reports the line that starts a step.
        /// </summary>
        public void StartStep( string name )
        {
            CurrentStep = name;
            _progress?.Report( new LoadProgressReport( name, name, CompletedSteps, TotalSteps ) );
        }

        /// <summary>
        /// Times a piece of the current step until the returned scope is disposed, then reports it as
        /// "  (Name: 1,234 ms)". It only reads the clock, so it never changes what loads.
        /// </summary>
        public IDisposable TimePart( string name )
        {
            return new PartScope( this, name, CurrentStep );
        }

        /// <summary>
        /// Counts a step as finished.
        /// </summary>
        public void FinishStep()
        {
            CompletedSteps++;
            CurrentStep = null;
        }

        /// <summary>
        /// A timed piece of a step, reported when it is disposed.
        /// </summary>
        private sealed class PartScope : IDisposable
        {
            private readonly LoadLog _log;
            private readonly string _name;
            private readonly string _stepName;
            private readonly Stopwatch _timer = Stopwatch.StartNew();

            public PartScope( LoadLog log, string name, string stepName )
            {
                _log = log;
                _name = name;
                _stepName = stepName;
            }

            public void Dispose()
            {
                var milliseconds = _timer.ElapsedMilliseconds;
                _log.WriteTiming( $"  ({_name}: {milliseconds:N0} ms)", new LoadTiming( LoadTimingKind.Part, _name, _stepName, milliseconds ) );
            }
        }
    }
}
