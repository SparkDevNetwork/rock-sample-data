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

using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Generation
{
    /*
        11/19/26 - CLAUDE

        Each Monday's staff update follows the church calendar (issue 9). A feature or article with an occasion runs on
        the Monday a set number of weeks before that occasion's week, so the Easter planning feature lands six weeks
        out, every year, on the dates the rest of the data uses. A Monday with no occasion feature takes the next
        evergreen feature, from a seeded order that uses each one before any repeats. Articles start with the week's
        occasion articles, then fill from the evergreen articles to the week's drawn count, one per author, at most
        three, the most Rock's article list allows (decision D-homepage-1).

        Reason: A real staff homepage talks about what is coming up, and the same seed and install day give the same updates.
    */

    /// <summary>
    /// One Monday's staff update: its feature and its one to three articles.
    /// </summary>
    internal sealed class PlannedStaffUpdate
    {
        public DateTime Monday { get; set; }

        public NewsFeature Feature { get; set; }

        public List<NewsArticle> Articles { get; set; } = new List<NewsArticle>();
    }

    /// <summary>
    /// Plans the weekly staff updates from the feature and article library.
    /// </summary>
    internal static class StaffUpdatePlanner
    {
        /// <summary>
        /// The most articles Rock's Internal Article List matrix holds.
        /// </summary>
        public const int MaximumArticles = 3;

        /// <param name="library">The features and articles.</param>
        /// <param name="articleCounts">How many articles an update has, as weights by count.</param>
        /// <param name="mondays">Each update's Monday, oldest first.</param>
        /// <param name="occasionDates">The dates of an occasion, or none for an unknown one.</param>
        /// <param name="seed">The load's seed.</param>
        public static List<PlannedStaffUpdate> Plan( InternalNewsLibrary library, IReadOnlyDictionary<string, int> articleCounts, IReadOnlyList<DateTime> mondays,
            Func<string, IReadOnlyList<DateTime>> occasionDates, string seed )
        {
            var features = ByMonday( library.Features, f => f.Occasion, f => f.WeeksBefore, occasionDates );
            var articles = ByMonday( library.Articles, a => a.Occasion, a => a.WeeksBefore, occasionDates );
            var random = new SeededRandom( $"{seed}:staff-updates" );
            var evergreenFeatures = new Rotation<NewsFeature>( library.EvergreenFeatures, random );
            var evergreenArticles = new Rotation<NewsArticle>( library.EvergreenArticles, random );
            var counts = articleCounts.ToDictionary( c => c.Key, c => ( double ) c.Value );

            var updates = new List<PlannedStaffUpdate>();
            foreach ( var monday in mondays )
            {
                var update = new PlannedStaffUpdate
                {
                    Monday = monday,
                    Feature = features.TryGetValue( monday, out var dated ) ? dated[0] : evergreenFeatures.Next( _ => true )
                };

                var target = Math.Min( MaximumArticles, int.Parse( random.Pick( counts ) ) );
                foreach ( var article in articles.TryGetValue( monday, out var datedArticles ) ? datedArticles : new List<NewsArticle>() )
                {
                    if ( update.Articles.Count < MaximumArticles && update.Articles.All( a => a.Author != article.Author ) )
                    {
                        update.Articles.Add( article );
                    }
                }

                while ( update.Articles.Count < target )
                {
                    var article = evergreenArticles.Next( a => update.Articles.All( chosen => chosen.Author != a.Author ) );
                    if ( article == null )
                    {
                        break;
                    }

                    update.Articles.Add( article );
                }

                updates.Add( update );
            }

            return updates;
        }

        /// <summary>
        /// The Monday of the week a date falls in. Weeks start on Monday, as the staff updates do.
        /// </summary>
        public static DateTime MondayOf( DateTime date ) => date.Date.AddDays( -( ( ( int ) date.DayOfWeek + 6 ) % 7 ) );

        /// <summary>
        /// Each dated piece on the Monday it runs, in file order.
        /// </summary>
        private static Dictionary<DateTime, List<T>> ByMonday<T>( IEnumerable<T> pieces, Func<T, string> occasion, Func<T, int> weeksBefore, Func<string, IReadOnlyList<DateTime>> occasionDates )
        {
            var byMonday = new Dictionary<DateTime, List<T>>();
            foreach ( var piece in pieces )
            {
                foreach ( var date in occasionDates( occasion( piece ) ) )
                {
                    var monday = MondayOf( date ).AddDays( -7 * weeksBefore( piece ) );
                    if ( !byMonday.TryGetValue( monday, out var list ) )
                    {
                        list = byMonday[monday] = new List<T>();
                    }

                    list.Add( piece );
                }
            }

            return byMonday;
        }

        /// <summary>
        /// Hands out items in a seeded order, each once before any repeats.
        /// </summary>
        private sealed class Rotation<T> where T : class
        {
            private readonly List<T> _items;
            private readonly SeededRandom _random;
            private List<T> _round = new List<T>();

            public Rotation( List<T> items, SeededRandom random )
            {
                _items = items;
                _random = random;
            }

            /// <summary>
            /// The next item that fits, or null when none does.
            /// </summary>
            public T Next( Func<T, bool> fits )
            {
                if ( _round.Count == 0 || !_round.Any( fits ) )
                {
                    var fresh = _items.Where( i => !_round.Contains( i ) ).ToList();
                    _round.AddRange( fresh.OrderBy( _ => _random.Between( 0, int.MaxValue - 1 ) ).ToList() );
                }

                var next = _round.FirstOrDefault( fits );
                if ( next != null )
                {
                    _round.Remove( next );
                }

                return next;
            }
        }
    }
}
