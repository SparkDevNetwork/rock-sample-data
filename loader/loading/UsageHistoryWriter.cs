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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        10/30/26 - CLAUDE

        A church's data views and reports were made over the years and are run every week, so each one gets the run
        history Rock keeps on it: when it was made, its run count since then, and when it last ran. Rock shows these on
        every view ("12 Runs Since 3/2/2025", "Last Run"), and an install where nothing was ever run does not look real.
        A view is made after the views it points at, and a report after its view. Run time stays empty: Rock measures it
        the first time the view runs on this server. One view in the configuration has not run in over a year, so Rock's
        advice to remove unused data views has an example (decision D-reporting-11).

        Reason: Question 9 in docs/reporting.md, settled yes.
    */

    /// <summary>
    /// Gives the church's reporting views and reports their run history.
    /// </summary>
    internal sealed class UsageHistoryWriter
    {
        /// <summary>
        /// A view that points at no other view was made this many days before install, at most and at least. A view or
        /// report built on others was made up to LaterDays after the newest of them. Either can say otherwise.
        /// </summary>
        private const int OldestDays = 1095;
        private const int NewestDays = 700;
        private const int LaterDays = 120;

        /// <summary>
        /// How often staff run one, in runs a week, at least and at most.
        /// </summary>
        private const double FewestRunsAWeek = 0.2;
        private const double MostRunsAWeek = 3;

        /// <summary>
        /// It last ran this many days before install, at most, unless it says otherwise.
        /// </summary>
        private const int LastRunDays = 13;

        private readonly RockDatabase _db;
        private readonly DateTime _today;
        private readonly string _seed;

        public UsageHistoryWriter( RockDatabase db, AnchorDates dates, string seed )
        {
            _db = db;
            _today = dates.Anchor.Date;
            _seed = seed;
        }

        /// <param name="views">The reporting views in an order where each comes after every view it points at.</param>
        /// <param name="references">The keys of the views each view points at.</param>
        /// <returns>How many views and reports got a history.</returns>
        public (int Views, int Reports) Write( List<DataViewDefinition> views, Func<DataViewDefinition, IEnumerable<string>> references, List<ReportDefinition> reports )
        {
            var made = new Dictionary<string, DateTime>();
            foreach ( var view in views )
            {
                var id = _db.IdByGuid( "DataView", view.Guid );
                if ( id == null )
                {
                    continue;
                }

                var after = references( view ).Where( made.ContainsKey ).Select( k => made[k] ).DefaultIfEmpty( DateTime.MinValue ).Max();
                made[view.Key] = Write( "DataView", id.Value, $"view:{view.Key}", view.Usage, after );
            }

            var reportCount = 0;
            foreach ( var report in reports )
            {
                var id = _db.IdByGuid( "Report", report.Guid );
                if ( id != null )
                {
                    Write( "Report", id.Value, $"report:{report.Key}", report.Usage, made.GetValueOrDefault( report.DataView, DateTime.MinValue ) );
                    reportCount++;
                }
            }

            return (made.Count, reportCount);
        }

        /// <summary>
        /// Stamps one view or report, and its filters or columns, as made on its day. Returns when it was made.
        /// </summary>
        /// <param name="after">It was made after this: when the views it points at were made.</param>
        private DateTime Write( string table, int id, string key, UsageHistory usage, DateTime after )
        {
            var random = new SeededRandom( $"{_seed}:reporting-usage:{key}" );
            var day = usage?.CreatedDaysAgo != null ? _today.AddDays( -usage.CreatedDaysAgo.Value )
                : after == DateTime.MinValue ? _today.AddDays( -random.Between( NewestDays, OldestDays ) )
                : after.Date.AddDays( random.Between( 1, LaterDays ) );
            if ( day <= after.Date )
            {
                day = after.Date.AddDays( 1 );
            }

            var made = OfficeHours( day < _today ? day : _today.AddDays( -1 ), random );

            var lastRun = OfficeHours( _today.AddDays( -( usage?.LastRunDaysAgo ?? random.Between( 1, LastRunDays ) ) ), random );
            if ( lastRun <= made )
            {
                lastRun = made.AddMinutes( random.Between( 5, 90 ) );
            }

            var weeks = ( lastRun - made ).TotalDays / 7;
            var runs = usage?.Runs ?? Math.Max( 1, ( int ) Math.Round( weeks * random.Between( FewestRunsAWeek, MostRunsAWeek ) ) );

            // Rock starts a new view's run count when it is saved (DataView.SaveHook), so "Runs Since" counts from then.
            var refresh = table == "DataView" ? ", [RunCountLastRefreshDateTime] = @Made" : string.Empty;
            _db.Execute( $@"
UPDATE [{table}] SET [CreatedDateTime] = @Made, [ModifiedDateTime] = @Made, [RunCount] = @Runs, [LastRunDateTime] = @LastRun{refresh} WHERE [Id] = @Id",
                P( "@Made", made ), P( "@Runs", runs ), P( "@LastRun", lastRun ), P( "@Id", id ) );
            var parts = table == "DataView" ? "UPDATE [DataViewFilter] SET [CreatedDateTime] = @Made, [ModifiedDateTime] = @Made WHERE [DataViewId] = @Id"
                : "UPDATE [ReportField] SET [CreatedDateTime] = @Made, [ModifiedDateTime] = @Made WHERE [ReportId] = @Id";
            _db.Execute( parts, P( "@Made", made ), P( "@Id", id ) );
            return made;
        }

        /// <summary>
        /// A time on the day during staff hours, from 8 AM to 5 PM.
        /// </summary>
        private static DateTime OfficeHours( DateTime day, SeededRandom random )
        {
            return day.Date.AddMinutes( random.Between( 8 * 60, 17 * 60 - 1 ) );
        }
    }
}
