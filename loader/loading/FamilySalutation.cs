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
        10/19/26 - CLAUDE

        A line-for-line port of GroupService.CalculateFamilySalutation (Rock/Model/Group/Group/GroupService.cs)
        with Rock's default arguments, plus the Truncate and ReplaceLastOccurrence string extensions it uses
        (Rock.Common/ExtensionMethods/StringExtensions.cs). Rock sets a family's GroupSalutation and
        GroupSalutationFull in the person and group member save hooks, which bulk copy skips. Rock Cleanup
        only fills in missing salutations and never checks existing ones, so these must match Rock exactly.

        Rock reads the members without an ORDER BY, so ties (two adults of one gender, two children of one
        age) come back in the order SQL Server reads the family's GroupMember rows: by GroupMember Id.

        Reason: Bulk copy skips the save hooks that set family salutations.
    */

    /// <summary>
    /// Rock's family salutation ("Ted & Cindy Decker") for a family's members.
    /// </summary>
    internal static class FamilySalutation
    {
        private const string Separator = ", ";
        private const string FinalSeparator = " & ";

        /// <summary>
        /// Rock's limit on GroupSalutation and GroupSalutationFull.
        /// </summary>
        private const int MaxLength = 250;

        /// <summary>
        /// Calculates the salutation. Members must be in GroupMember Id order.
        /// </summary>
        public static string Calculate( string familyName, IReadOnlyList<SalutationMember> members, bool includeChildren )
        {
            // Rock leaves out deceased members always, and inactive or pending people unless nobody else is left.
            var living = members.Where( m => !m.IsDeceased ).ToList();
            var active = living.Where( m => m.IsActiveRecord ).ToList();
            var list = includeChildren ? active : active.Where( m => m.IsAdult ).ToList();
            if ( list.Count == 0 )
            {
                list = active;
                if ( list.Count == 0 )
                {
                    list = living;
                }

                if ( list.Count == 0 )
                {
                    return Truncate( familyName );
                }
            }

            var names = new List<string>();
            var primaryLastName = string.Empty;
            var hasMultipleLastNames = list.Select( m => m.LastName ).Distinct().Count() > 1;

            var adults = list.Where( m => m.IsAdult ).OrderBy( m => m.Gender ).ToList();
            if ( adults.Count > 0 )
            {
                primaryLastName = adults[0].LastName;
                names.AddRange( adults.Select( a => hasMultipleLastNames ? $"{a.NickName} {a.LastName}" : a.NickName ) );
            }

            if ( includeChildren || adults.Count == 0 )
            {
                var children = list.Where( m => !m.IsAdult ).OrderByDescending( m => m.Age ).ToList();
                if ( children.Count > 0 )
                {
                    if ( string.IsNullOrWhiteSpace( primaryLastName ) )
                    {
                        primaryLastName = children[0].LastName;
                    }

                    names.AddRange( children.Select( c => hasMultipleLastNames ? $"{c.NickName} {c.LastName}" : c.NickName ) );
                }
            }

            var salutation = ReplaceLastOccurrence( string.Join( Separator, names ), Separator, FinalSeparator );
            if ( !hasMultipleLastNames )
            {
                salutation = salutation + " " + primaryLastName;
            }

            return Truncate( salutation );
        }

        /// <summary>
        /// Rock's ReplaceLastOccurrence, which leaves the text alone when the match is at position 0.
        /// </summary>
        private static string ReplaceLastOccurrence( string source, string find, string replace )
        {
            var place = source.LastIndexOf( find, StringComparison.CurrentCulture );
            return place > 0 ? source.Remove( place, find.Length ).Insert( place, replace ) : source;
        }

        /// <summary>
        /// Rock's Truncate with an ellipsis: cuts at the last space before the limit.
        /// </summary>
        private static string Truncate( string text )
        {
            if ( text == null || text.Length <= MaxLength )
            {
                return text;
            }

            var truncated = text.Substring( 0, MaxLength - 3 );
            var lastSpace = truncated.LastIndexOf( ' ' );
            if ( lastSpace > 0 )
            {
                truncated = truncated.Substring( 0, lastSpace );
            }

            return truncated + "...";
        }
    }

    /// <summary>
    /// What Rock's family salutation reads about one family member.
    /// </summary>
    internal sealed class SalutationMember
    {
        public string NickName { get; init; }

        public string LastName { get; init; }

        /// <summary>
        /// Rock's Gender value: 0 unknown, 1 male, 2 female. Adults sort by it.
        /// </summary>
        public int Gender { get; init; }

        /// <summary>
        /// Age on the anchor date. Children sort oldest first; no birth date sorts last.
        /// </summary>
        public int? Age { get; init; }

        /// <summary>
        /// True for the Adult family role, false for Child.
        /// </summary>
        public bool IsAdult { get; init; }

        /// <summary>
        /// True when the person's record status is Active.
        /// </summary>
        public bool IsActiveRecord { get; init; }

        public bool IsDeceased { get; init; }
    }
}
