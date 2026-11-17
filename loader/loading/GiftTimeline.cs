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

namespace RockSampleData.Loader.Loading
{
    /*
        11/16/26 - CLAUDE

        Rock's Giving Automation job runs every night, and its journey stage run every Tuesday. Each run reads the
        gifts as they stand that night. A refund changes a gift after the fact: a duplicate gift counted until it was
        refunded, and a partly refunded gift counted at its full amount until then. So the replays of those runs
        (giving attributes, journey stages, alerts, and Give steps) read the gifts as of each run, not as of install.

        Reason: A run between a gift and its refund saw the gift, and Rock kept what that run decided, such as an
        alert on the duplicate (decision 263).
    */

    /// <summary>
    /// A gift a refund changed: the gift as Rock saw it until the refund, and when the refund came.
    /// </summary>
    internal sealed record RefundedGift( string GivingId, DateTime RefundedAt, RockGift Before );

    /// <summary>
    /// Every qualifying gift, as Rock's Giving Automation job saw them over time.
    /// </summary>
    internal sealed class GiftTimeline
    {
        private static readonly List<RockGift> NoGifts = new List<RockGift>();

        private readonly Dictionary<string, List<RefundedGift>> _refunded;
        private readonly Dictionary<string, List<(DateTime From, List<RockGift> Gifts, List<DateTime> Dates)>> _versions = new Dictionary<string, List<(DateTime, List<RockGift>, List<DateTime>)>>();
        private readonly Dictionary<string, List<DateTime>> _datesNow = new Dictionary<string, List<DateTime>>();

        /// <param name="now">Every qualifying gift on install day, by giving id and in date order, with refunds applied.</param>
        /// <param name="refunded">Each gift a refund changed, as it was before the refund.</param>
        public GiftTimeline( Dictionary<string, List<RockGift>> now, IEnumerable<RefundedGift> refunded )
        {
            Now = now;
            _refunded = refunded.GroupBy( r => r.GivingId ).ToDictionary( g => g.Key, g => g.OrderBy( r => r.RefundedAt ).ToList() );
        }

        /// <summary>
        /// Every qualifying gift on install day, by giving id and in date order, with refunds applied.
        /// </summary>
        public Dictionary<string, List<RockGift>> Now { get; }

        /// <summary>
        /// Every giving id with a gift now or before a refund.
        /// </summary>
        public IEnumerable<string> GivingIds => Now.Keys.Union( _refunded.Keys );

        /// <summary>
        /// Every giving id with a gift a refund changed.
        /// </summary>
        public IReadOnlyCollection<string> RefundedGivingIds => _refunded.Keys;

        /// <summary>
        /// The unit's gifts in date order as Rock saw them at <paramref name="when"/>. A refund counts from the moment
        /// it was made.
        /// </summary>
        public List<RockGift> AsOf( string givingId, DateTime when )
        {
            if ( !_refunded.TryGetValue( givingId, out var refunds ) )
            {
                return Now.TryGetValue( givingId, out var gifts ) ? gifts : NoGifts;
            }

            return Version( givingId, refunds, when ).Gifts;
        }

        /// <summary>
        /// The dates of <see cref="AsOf"/>.
        /// </summary>
        public List<DateTime> DatesAsOf( string givingId, DateTime when )
        {
            if ( !_refunded.TryGetValue( givingId, out var refunds ) )
            {
                if ( !_datesNow.TryGetValue( givingId, out var dates ) )
                {
                    dates = _datesNow[givingId] = ( Now.TryGetValue( givingId, out var gifts ) ? gifts : NoGifts ).Select( g => g.When ).OrderBy( d => d ).ToList();
                }

                return dates;
            }

            return Version( givingId, refunds, when ).Dates;
        }

        /// <summary>
        /// The unit's gifts that Rock's nightly run saw arrive: each gift that still counted on the first 10pm on or
        /// after it. A gift refunded in full before that night never counted.
        /// </summary>
        public IEnumerable<RockGift> Arrived( string givingId )
        {
            if ( !_refunded.TryGetValue( givingId, out var refunds ) )
            {
                return Now.TryGetValue( givingId, out var gifts ) ? gifts : NoGifts;
            }

            return ( Now.TryGetValue( givingId, out var now ) ? now : NoGifts )
                .Concat( refunds.Select( r => r.Before ) )
                .GroupBy( g => g.Id )
                .Select( g => g.First() )
                .Where( g => AsOf( givingId, NightOf( g.When ) ).Any( x => x.Id == g.Id ) )
                .OrderBy( g => g.When ).ThenBy( g => g.Id )
                .ToList();
        }

        /// <summary>
        /// Rock's first nightly run (10pm) on or after <paramref name="when"/>.
        /// </summary>
        public static DateTime NightOf( DateTime when ) => when <= when.Date.AddHours( 22 ) ? when.Date.AddHours( 22 ) : when.Date.AddDays( 1 ).AddHours( 22 );

        /// <summary>
        /// The unit's gifts from the last refund at or before <paramref name="when"/> until the next one: the gifts now,
        /// with each later refund's gift put back as it was.
        /// </summary>
        private (List<RockGift> Gifts, List<DateTime> Dates) Version( string givingId, List<RefundedGift> refunds, DateTime when )
        {
            if ( !_versions.TryGetValue( givingId, out var versions ) )
            {
                versions = new List<(DateTime, List<RockGift>, List<DateTime>)>();
                for ( var applied = 0; applied <= refunds.Count; applied++ )
                {
                    var restored = refunds.Skip( applied ).Select( r => r.Before ).ToDictionary( g => g.Id );
                    var gifts = ( Now.TryGetValue( givingId, out var now ) ? now : NoGifts )
                        .Where( g => !restored.ContainsKey( g.Id ) )
                        .Concat( restored.Values )
                        .OrderBy( g => g.When ).ThenBy( g => g.Id )
                        .ToList();
                    versions.Add( (applied == 0 ? DateTime.MinValue : refunds[applied - 1].RefundedAt, gifts, gifts.Select( g => g.When ).ToList()) );
                }

                _versions[givingId] = versions;
            }

            var index = versions.Count - 1;
            while ( index > 0 && versions[index].From > when )
            {
                index--;
            }

            return (versions[index].Gifts, versions[index].Dates);
        }
    }
}
