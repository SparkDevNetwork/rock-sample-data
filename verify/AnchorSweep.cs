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

using RockSampleData.Loader;
using RockSampleData.Loader.Packages;

namespace RockSampleData.Verify
{
    /*
        10/30/26 - CLAUDE

        Every date in a load counts back from its install day, so a Cast story can hold on one day and not the
        next. Margaret Carter's monthly giving held when verify ran on 2026-10-29, and Rock showed her as
        undetermined on an install the next day: that day's draw put two of her gifts a week apart. A verify run
        checks one install day, so it cannot catch this.

        The sweep dry-runs the giving part of one dataset once for each install day in a range, with the Cast
        expectations checked inside each load's transaction, and reports every expectation that fails on any day.
        A dry run rolls back, so one copy of the trusted base serves every day.

        Reason: Cast giving stories must hold on every install day, not only the day verify ran (decision D-anchors-1).
    */

    /// <summary>
    /// Checks the Cast giving expectations of one dataset on each install day in a range.
    /// </summary>
    internal static class AnchorSweep
    {
        /// <summary>
        /// How many install days a normal verify run sweeps, from its anchor.
        /// </summary>
        public const int DefaultDays = 60;

        /// <summary>
        /// Dry-runs <paramref name="package"/>'s giving part into a copy of <paramref name="baseDatabase"/> once for each of
        /// <paramref name="days"/> install days from <paramref name="firstDay"/>, prints each day's result, and returns the
        /// anchor-sweep check: each Cast expectation that failed, with the days it failed on.
        /// </summary>
        public static CheckResult Run( SqlServer sql, string baseDatabase, string database, SampleDataPackage package, RockRelease rock, string rockVersion, DateTime firstDay, int days, string seed, bool isKept )
        {
            Console.WriteLine( $"\nAnchor sweep: {package.Manifest.Dataset}'s Cast giving expectations on {days} install days from {firstDay:yyyy-MM-dd}" );
            sql.Copy( baseDatabase, database );
            var failingDays = 0;
            var failuresByStory = new SortedDictionary<string, List<DateTime>>( StringComparer.Ordinal );
            try
            {
                for ( var day = firstDay; day < firstDay.AddDays( days ); day = day.AddDays( 1 ) )
                {
                    var lines = new LineCollector();
                    DatasetLoader.Load( sql.ConnectionString( database ), package, new DatasetLoadOptions
                    {
                        Anchor = day,
                        Seed = seed,
                        Parts = new[] { SampleDataPart.Giving },
                        Progress = lines,
                        RockWebConfigPath = rock.WebConfigPath,
                        RockVersion = rockVersion,
                        IsCastExpectationsChecked = true,
                        IsDryRun = true
                    } );

                    var (checks, failures) = lines.CastExpectations();
                    Console.WriteLine( failures.Count == 0 ? $"  {day:yyyy-MM-dd}: all {checks} hold" : $"  {day:yyyy-MM-dd}: {failures.Count} of {checks} do not hold" );
                    foreach ( var failure in failures )
                    {
                        Console.WriteLine( $"      {failure}" );
                        if ( !failuresByStory.TryGetValue( failure.Split( ',' )[0], out var failedDays ) )
                        {
                            failedDays = failuresByStory[failure.Split( ',' )[0]] = new List<DateTime>();
                        }

                        failedDays.Add( day );
                    }

                    failingDays += failures.Count > 0 ? 1 : 0;
                }
            }
            finally
            {
                if ( !isKept )
                {
                    sql.Drop( database );
                }
            }

            var problems = failuresByStory.Select( story => $"{story.Key}: fails on {story.Value.Count} of {days} days ({string.Join( ", ", story.Value.Select( d => d.ToString( "yyyy-MM-dd" ) ) )})" ).ToList();
            return CheckResult.From( "anchor-sweep", $"{days - failingDays} of {days} install days from {firstDay:yyyy-MM-dd} hold every Cast giving expectation", problems );
        }

        /// <summary>
        /// Keeps a load's progress lines, so the Cast expectations step's result can be read back.
        /// </summary>
        private sealed class LineCollector : IProgress<LoadProgressReport>
        {
            private readonly List<string> _lines = new List<string>();

            public void Report( LoadProgressReport value )
            {
                _lines.Add( value.Message );
            }

            /// <summary>
            /// The number of expectations checked and each failure, from the lines CastExpectations.Check writes:
            /// "  Cast expectations: all N hold", or "  WARNING: F of N Cast expectations do not hold" and one
            /// indented line per failure.
            /// </summary>
            public (int Checks, List<string> Failures) CastExpectations()
            {
                var failures = new List<string>();
                for ( var i = 0; i < _lines.Count; i++ )
                {
                    var line = _lines[i].Trim();
                    if ( line.StartsWith( "Cast expectations: all ", StringComparison.Ordinal ) )
                    {
                        return (int.Parse( line.Split( ' ' )[3] ), failures);
                    }

                    if ( line.StartsWith( "WARNING: ", StringComparison.Ordinal ) && line.EndsWith( "Cast expectations do not hold", StringComparison.Ordinal ) )
                    {
                        for ( var j = i + 1; j < _lines.Count && _lines[j].StartsWith( "    ", StringComparison.Ordinal ); j++ )
                        {
                            failures.Add( _lines[j].Trim() );
                        }

                        return (int.Parse( line.Split( ' ' )[3] ), failures);
                    }
                }

                throw new InvalidOperationException( "The load did not report its Cast expectations." );
            }
        }
    }
}
