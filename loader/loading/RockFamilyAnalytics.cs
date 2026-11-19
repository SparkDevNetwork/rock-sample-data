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

using System.Globalization;

using RockSampleData.Loader.Database;

namespace RockSampleData.Loader.Loading
{
    /*
        11/19/26 - CLAUDE

        A line-for-line port of how Rock's Family Analytics job moves families in and out of eRA, so the loader can
        replay it every week over the history window. Keep it a faithful copy: same windows, same counts, same
        order of writes, and the same quirks. Do not "improve" it.

        Rock sources this mirrors (paths in the SparkDevNetwork/Rock repository; the same in Rock 20.0.10 and 21.0.2):
        - The installed spCrm_FamilyAnalyticsEraDataset (Rock/Plugin/HotFixes/311_FixEraFamilyAnalyticsWeekBoundariesFollowup6902):
          the windows, which adults count, giving by giving id and attendance by family, and its side effect, which
          gives Currently an eRA and eRA Start Date to everyone whose primary family has an eRA adult.
        - The installed ufnUtility_GetSundayDate and ufnUtility_GetPreviousSundayDate (RockSundays).
        - Rock/Jobs/CalculateFamilyAnalytics.cs (Execute): the exit and entry rules and what each writes, with
          Write eRA History on, as Rock ships it.
        - Rock/Model/Engagement/Step/Step.SaveHook.cs: the person history each eRA step adds, and the Core Steps
          completion its first exit makes (UpdateStepProgramCompletion).

        Quirks kept on purpose:
        - The procedure counts every contribution with a tax-deductible line, refunds included.
        - An exit touches every member of the family, even one who never had the flag, and writes each an
          "Exited" history line.
        - An entry skips deceased and inactive members, but the next run's procedure gives them the flag and a start
          date anyway, with no step and no history.
        - The procedure writes the start date with the time (CONVERT 127). The job writes a date only.

        The job launches no workflows, because the sample church sets no eRA entry or exit workflow.

        The version guard (validation/rock-lock.json) fails when any of these change in Rock.

        Reason: eRA on a fresh install must be what Rock itself would show if the job had run every week.
    */

    /// <summary>
    /// The port of Rock's eRA rule. Nothing here touches a database.
    /// </summary>
    internal static class RockFamilyAnalytics
    {
        // spCrm_FamilyAnalyticsEraDataset's configuration.
        public const int EntryGivingDurationLongWeeks = 52;
        public const int EntryGivingDurationShortWeeks = 6;
        public const int EntryAttendanceDurationWeeks = 16;
        public const int ExitGivingDurationWeeks = 8;
        public const int ExitAttendanceDurationShortWeeks = 4;
        public const int ExitAttendanceDurationLongWeeks = 16;
        public const int EntryGiftCountDurationLong = 4;
        public const int EntryGiftCountDurationShort = 1;
        public const int EntryAttendanceCountDuration = 8;

        // CalculateFamilyAnalytics.Execute's configuration.
        public const int ExitGivingCount = 1;
        public const int ExitAttendanceCountShort = 1;
        public const int ExitAttendanceCountLong = 8;

        /// <summary>
        /// One run of Rock's job at <paramref name="now"/>: the procedure's side effect, its dataset, and the job's
        /// exits and entries, applied to <paramref name="church"/>. Returns the dataset rows the job acted on.
        /// </summary>
        public static List<EraResult> Run( EraChurch church, DateTime now )
        {
            var previousSunday = RockSundays.PreviousSunday( now );

            // @TempPersonFamilyGroupIds: every adult in a family, whatever their record status, with their eRA flag as the run starts.
            var adultRows = church.Families.Values
                .Where( f => !f.IsArchived )
                .SelectMany( f => f.Members.Where( m => m.IsAdult && !m.IsArchived ).Select( m => (Family: f, Person: church.People[m.PersonId]) ) )
                .Where( r => r.Person.IsPersonRecord )
                .Select( r => (r.Family, r.Person, IsEra: r.Person.Flag != null && string.Equals( r.Person.Flag.Value, "true", StringComparison.OrdinalIgnoreCase )) )
                .ToList();

            // The procedure's MERGEs: everyone whose primary family has an eRA adult gets the flag and a start date.
            var eraFamilyIds = adultRows.Where( r => r.IsEra ).Select( r => r.Family.Id ).ToHashSet();
            foreach ( var person in church.People.Values.Where( p => p.PrimaryFamilyId.HasValue && eraFamilyIds.Contains( p.PrimaryFamilyId.Value ) ).OrderBy( p => p.Id ) )
            {
                if ( person.Flag == null )
                {
                    person.Flag = new EraValue( "True", now );
                }
                else if ( !string.Equals( person.Flag.Value, "True", StringComparison.OrdinalIgnoreCase ) )
                {
                    person.Flag.Set( "True", now );
                }

                if ( person.Start == null )
                {
                    person.Start = new EraValue( ProcedureDateText( now ), now ) { AsDateTime = now };
                }
                else if ( string.IsNullOrEmpty( person.Start.Value ) )
                {
                    person.Start.Set( ProcedureDateText( now ), now );
                    person.Start.AsDateTime = now;
                }
            }

            // The dataset: each qualifying adult's counts, then the most of each count per family.
            var results = adultRows
                .Select( r => (r.Family, r.IsEra, Counts: Counts( church, r.Person.GivingId, r.Family.Id, previousSunday )) )
                .Where( r => r.IsEra || MeetsEntry( r.Counts ) )
                .GroupBy( r => r.Family.Id )
                .OrderBy( g => g.Key )
                .Select( g => new EraResult(
                    g.Key,
                    g.Max( r => r.Counts.EntryGiftCountDurationShort ),
                    g.Max( r => r.Counts.EntryGiftCountDurationLong ),
                    g.Max( r => r.Counts.ExitGiftCountDuration ),
                    g.Max( r => r.Counts.EntryAttendanceCountDuration ),
                    g.Max( r => r.Counts.ExitAttendanceCountDurationShort ),
                    g.Max( r => r.Counts.ExitAttendanceCountDurationLong ),
                    g.Any( r => r.IsEra ) ) )
                .ToList();

            foreach ( var result in results )
            {
                var family = church.Families[result.FamilyId];
                if ( result.IsEra )
                {
                    if ( MeetsExit( result ) )
                    {
                        foreach ( var person in family.Members.Select( m => church.People[m.PersonId] ) )
                        {
                            Exit( person, now );
                        }
                    }
                }
                else
                {
                    foreach ( var person in family.Members.Select( m => church.People[m.PersonId] ).Where( p => !p.IsDeceased && !p.IsInactive ) )
                    {
                        Enter( person, family, now );
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Whether a family that is not an eRA enters: four gifts in a year with one in the last six weeks, or eight Sundays in sixteen weeks.
        /// </summary>
        public static bool MeetsEntry( EraCounts counts )
        {
            return ( counts.EntryGiftCountDurationLong >= EntryGiftCountDurationLong && counts.EntryGiftCountDurationShort >= EntryGiftCountDurationShort )
                || counts.EntryAttendanceCountDuration >= EntryAttendanceCountDuration;
        }

        /// <summary>
        /// Whether an eRA family exits: no gift in eight weeks, no Sunday in four, and fewer than eight in sixteen.
        /// </summary>
        public static bool MeetsExit( EraResult result )
        {
            return result.ExitGiftCountDuration < ExitGivingCount
                && result.ExitAttendanceCountDurationShort < ExitAttendanceCountShort
                && result.ExitAttendanceCountDurationLong < ExitAttendanceCountLong;
        }

        /// <summary>
        /// The procedure's counts for one adult: gifts by their giving id, and Sundays by their family, each in a window
        /// that ends on the previous Sunday.
        /// </summary>
        private static EraCounts Counts( EraChurch church, string givingId, int familyId, DateTime previousSunday )
        {
            var gifts = givingId != null && church.GiftSundaysByGivingId.TryGetValue( givingId, out var g ) ? g : Array.Empty<DateTime>();
            var sundays = church.AttendedSundaysByFamily.TryGetValue( familyId, out var s ) ? s : Array.Empty<DateTime>();

            // Each list is sorted, so a window's count is the distance between two binary searches.
            int Within( DateTime[] dates, int weeks ) => CountAfter( dates, previousSunday.AddDays( -7 * weeks ) ) - CountAfter( dates, previousSunday );

            return new EraCounts(
                Within( gifts, EntryGivingDurationShortWeeks ),
                Within( gifts, EntryGivingDurationLongWeeks ),
                Within( gifts, ExitGivingDurationWeeks ),
                Within( sundays, EntryAttendanceDurationWeeks ),
                Within( sundays, ExitAttendanceDurationShortWeeks ),
                Within( sundays, ExitAttendanceDurationLongWeeks ) );
        }

        /// <summary>
        /// How many dates in a sorted array are after <paramref name="date"/>.
        /// </summary>
        private static int CountAfter( DateTime[] dates, DateTime date )
        {
            int low = 0, high = dates.Length;
            while ( low < high )
            {
                var middle = ( low + high ) / 2;
                if ( dates[middle] <= date )
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            return dates.Length - low;
        }

        /// <summary>
        /// The job's exit for one family member: the flag goes, eRA End Date is today, an "Exited" history line, and
        /// their open eRA step completes, which makes their Core Steps completion if they have none.
        /// </summary>
        private static void Exit( EraPerson person, DateTime now )
        {
            person.Flag = null;
            if ( person.End == null )
            {
                person.End = new EraValue( IsoDate( now ), now );
            }
            else
            {
                person.End.Set( IsoDate( now ), now );
            }

            person.History.Add( new EraHistory( now, IsEntry: false ) );

            if ( person.PrimaryAliasId.HasValue )
            {
                var step = person.Steps.FirstOrDefault( s => s.Completed == null );
                if ( step != null )
                {
                    step.Completed = now;
                    person.CompletionStep ??= step;
                }
            }
        }

        /// <summary>
        /// The job's entry for one family member: the flag, a start date unless they were already an eRA, no end date,
        /// an "Entered" history line when they were not already an eRA, and a new In Progress eRA step.
        /// </summary>
        private static void Enter( EraPerson person, EraFamily family, DateTime now )
        {
            var isCurrentlyEra = person.Flag != null && string.Equals( person.Flag.Value, "True", StringComparison.OrdinalIgnoreCase );
            if ( person.Flag == null )
            {
                person.Flag = new EraValue( "True", now );
            }
            else
            {
                person.Flag.Set( "True", now );
            }

            if ( person.Start == null )
            {
                person.Start = new EraValue( IsoDate( now ), now ) { AsDateTime = now.Date };
            }
            else if ( !isCurrentlyEra || !person.Start.AsDateTime.HasValue )
            {
                person.Start.Set( IsoDate( now ), now );
                person.Start.AsDateTime = now.Date;
            }

            person.End = null;

            if ( !isCurrentlyEra )
            {
                person.History.Add( new EraHistory( now, IsEntry: true ) );
            }

            if ( person.PrimaryAliasId.HasValue )
            {
                person.Steps.Add( new EraStep( now, family.CampusId ) );
            }
        }

        /// <summary>
        /// Rock's ToISO8601DateString, which the job writes.
        /// </summary>
        private static string IsoDate( DateTime date ) => date.ToString( "yyyy-MM-dd", CultureInfo.InvariantCulture );

        /// <summary>
        /// SQL Server's CONVERT( NVARCHAR(MAX), datetime, 127 ), which the procedure writes: milliseconds only when there are some.
        /// </summary>
        private static string ProcedureDateText( DateTime date ) => date.Millisecond == 0 ? date.ToString( "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture ) : date.ToString( "yyyy-MM-dd'T'HH:mm:ss.fff", CultureInfo.InvariantCulture );
    }

    /// <summary>
    /// The procedure's counts for one adult.
    /// </summary>
    internal sealed record EraCounts( int EntryGiftCountDurationShort, int EntryGiftCountDurationLong, int ExitGiftCountDuration,
        int EntryAttendanceCountDuration, int ExitAttendanceCountDurationShort, int ExitAttendanceCountDurationLong );

    /// <summary>
    /// One row of the procedure's dataset: a family, the most of each count among its qualifying adults, and whether one is an eRA.
    /// </summary>
    internal sealed record EraResult( int FamilyId, int EntryGiftCountDurationShort, int EntryGiftCountDurationLong, int ExitGiftCountDuration,
        int EntryAttendanceCountDuration, int ExitAttendanceCountDurationShort, int ExitAttendanceCountDurationLong, bool IsEra );

    /// <summary>
    /// What the job reads: every family and person, each giving id's gift Sundays, and each family's attended Sundays.
    /// </summary>
    internal sealed class EraChurch
    {
        public Dictionary<int, EraFamily> Families { get; } = new Dictionary<int, EraFamily>();

        public Dictionary<int, EraPerson> People { get; } = new Dictionary<int, EraPerson>();

        /// <summary>
        /// The SundayDate of each contribution with a tax-deductible line, by the giving id of the person who gave it, sorted.
        /// </summary>
        public Dictionary<string, DateTime[]> GiftSundaysByGivingId { get; } = new Dictionary<string, DateTime[]>( StringComparer.OrdinalIgnoreCase );

        /// <summary>
        /// The distinct Sundays any member of the family attended a weekend service group, by family, sorted.
        /// </summary>
        public Dictionary<int, DateTime[]> AttendedSundaysByFamily { get; } = new Dictionary<int, DateTime[]>();
    }

    internal sealed class EraFamily
    {
        public int Id { get; init; }

        public int? CampusId { get; init; }

        public bool IsArchived { get; init; }

        /// <summary>
        /// Every membership in the family, archived ones too, as the job's Members does.
        /// </summary>
        public List<EraMember> Members { get; } = new List<EraMember>();
    }

    internal sealed record EraMember( int PersonId, bool IsAdult, bool IsArchived );

    internal sealed class EraPerson
    {
        public int Id { get; init; }

        public int? PrimaryFamilyId { get; init; }

        public int? PrimaryAliasId { get; init; }

        public string GivingId { get; init; }

        public bool IsPersonRecord { get; init; }

        public bool IsInactive { get; init; }

        public bool IsDeceased { get; init; }

        /// <summary>Currently an eRA, or null when the person has no value.</summary>
        public EraValue Flag { get; set; }

        /// <summary>eRA Start Date, or null when the person has no value.</summary>
        public EraValue Start { get; set; }

        /// <summary>eRA End Date, or null when the person has no value.</summary>
        public EraValue End { get; set; }

        public List<EraStep> Steps { get; } = new List<EraStep>();

        public List<EraHistory> History { get; } = new List<EraHistory>();

        /// <summary>
        /// The eRA step whose exit made the person's Core Steps completion: their first completed one.
        /// </summary>
        public EraStep CompletionStep { get; set; }
    }

    /// <summary>
    /// An attribute value row: its text, the date Rock reads from it, and when the row was made and last changed.
    /// </summary>
    internal sealed class EraValue
    {
        public EraValue( string value, DateTime created )
        {
            Value = value;
            Created = created;
            Modified = created;
        }

        public string Value { get; private set; }

        public DateTime? AsDateTime { get; set; }

        public DateTime Created { get; }

        public DateTime Modified { get; private set; }

        public void Set( string value, DateTime modified )
        {
            if ( value != Value )
            {
                Value = value;
                Modified = modified;
            }
        }
    }

    internal sealed class EraStep
    {
        public EraStep( DateTime start, int? campusId )
        {
            Start = start;
            CampusId = campusId;
        }

        public DateTime Start { get; }

        public int? CampusId { get; }

        /// <summary>When the job completed it on an exit, or null while it is In Progress.</summary>
        public DateTime? Completed { get; set; }
    }

    internal sealed record EraHistory( DateTime At, bool IsEntry );
}
