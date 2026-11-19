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
        11/18/26 - CLAUDE

        Rock's reminder blocks save a reminder with a date-only due date, RenewCurrentCount 0, and the owner's primary
        alias. A Personal Reminder points at the person's primary alias, and a Group Reminder at the group. Completing a
        repeating reminder does not complete it: Rock moves its date to the completion time plus its period and counts
        one more repeat, so a repeating reminder's date carries the time of day it was last completed.

        Rock's Process Reminders job sets each owner's ReminderCount to their reminders that are due (dated now or
        earlier) and not complete. The loader sets the same count, and every reminder it writes is due by midnight on
        the install day or dated tomorrow or later, so the job's first run on the install day changes nothing. An owner
        whose reminders were all due once and are now complete keeps 0, as Rock's save hook leaves them. An owner whose
        reminders have never come due keeps no count, as Rock does.

        Staff set reminders about generated people they wrote notes on, read back from the notes step's rows, and
        generated leaders set group reminders, read back from the small groups step's rows. Reminders therefore need
        notes and small groups (SampleDataParts.All). The Cast's reminders are hand-written in configuration/reminders.json.

        Reason: Issue 22 (decisions 295 to 298).
    */

    /// <summary>
    /// Writes the reminders staff and leaders set for themselves, and each owner's reminder count.
    /// </summary>
    internal sealed class RemindersLoader
    {
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public RemindersLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// A reminder as Rock stores it.
        /// </summary>
        internal sealed class PlannedReminder
        {
            public bool IsGroup { get; set; }

            public int OwnerAliasId { get; set; }

            public int EntityId { get; set; }

            public string Note { get; set; }

            public DateTime ReminderDate { get; set; }

            public bool IsComplete { get; set; }

            public int? RenewPeriodDays { get; set; }

            public int? RenewMaxCount { get; set; }

            public int RenewCurrentCount { get; set; }

            public DateTime Created { get; set; }

            public DateTime Modified { get; set; }

            public Guid Guid { get; set; }

            public bool IsCast { get; set; }
        }

        /// <summary>
        /// Plans and writes every reminder, then sets each owner's reminder count.
        /// </summary>
        /// <param name="people">Everyone loaded.</param>
        /// <param name="castHouseholdKeys">The Cast's households, whose people get only their hand-written reminders.</param>
        /// <param name="smallGroupTypeId">The small group type, whose generated groups get group reminders.</param>
        public void Load( PeopleLoader people, HashSet<string> castHouseholdKeys, int smallGroupTypeId )
        {
            var config = _source.Reminders;
            if ( config == null )
            {
                _db.Log.Write( "  Skipped (this package has no configuration/reminders.json)" );
                return;
            }

            var byPersonId = people.Households.SelectMany( h => h.Members ).GroupBy( p => p.PersonId ).ToDictionary( g => g.Key, g => g.First() );
            var castPersonIds = people.Households.Where( h => castHouseholdKeys.Contains( h.Cast.Key ) ).SelectMany( h => h.Members ).Select( p => p.PersonId ).ToHashSet();

            var reminders = PlanCast( config, people );
            reminders.AddRange( PlanPersonReminders( config, byPersonId, castPersonIds ) );
            reminders.AddRange( PlanGroupReminders( config, byPersonId, castPersonIds, smallGroupTypeId ) );
            Write( config, reminders );
            var counted = UpdateReminderCounts();

            var tomorrow = _dates.Anchor.Date.AddDays( 1 );
            var due = reminders.Count( r => !r.IsComplete && r.ReminderDate < tomorrow );
            var upcoming = reminders.Count( r => !r.IsComplete && r.ReminderDate >= tomorrow );
            _db.Log.Write( $"  Reminders: {reminders.Count:N0} ({reminders.Count( r => r.IsCast ):N0} Cast; {reminders.Count( r => !r.IsGroup ):N0} personal, {reminders.Count( r => r.IsGroup ):N0} group; "
                + $"{due:N0} due, {upcoming:N0} upcoming, {reminders.Count( r => r.IsComplete ):N0} complete, {reminders.Count( r => r.RenewPeriodDays.HasValue ):N0} repeating)" );
            _db.Log.Write( $"  Reminder counts: {counted:N0} people with reminders due" );
        }

        #region Planning

        /// <summary>
        /// The Cast's hand-written reminders, exactly as their file describes them.
        /// </summary>
        private List<PlannedReminder> PlanCast( RemindersConfiguration config, PeopleLoader people )
        {
            var planned = new List<PlannedReminder>();
            foreach ( var cast in config.Cast )
            {
                if ( !people.People.TryGetValue( cast.Owner, out var owner ) )
                {
                    continue;
                }

                int entityId;
                if ( cast.Person.HasValue )
                {
                    if ( !people.People.TryGetValue( cast.Person.Value, out var about ) )
                    {
                        continue;
                    }

                    entityId = about.AliasId;
                }
                else
                {
                    var groupId = _db.IdByGuid( "Group", cast.Group.Value );
                    if ( !groupId.HasValue )
                    {
                        continue;
                    }

                    entityId = groupId.Value;
                }

                var random = new SeededRandom( $"{_seed}:reminder:cast:{cast.Key}" );
                var set = Resolve( cast.Set ).Add( DayTime( random ) );
                var date = Resolve( cast.Date );
                var modified = set;
                if ( cast.Repeat != null && cast.Repeat.Completed > 0 )
                {
                    // A repeat's date is the last completion plus its period, at the time of day it was completed.
                    var completed = date.AddDays( -cast.Repeat.EveryDays ).Add( DayTime( random ) );
                    date = completed.AddDays( cast.Repeat.EveryDays );
                    modified = completed;
                }

                planned.Add( new PlannedReminder
                {
                    IsGroup = !cast.Person.HasValue,
                    OwnerAliasId = owner.AliasId,
                    EntityId = entityId,
                    Note = cast.Note,
                    ReminderDate = date,
                    IsComplete = cast.Expect == "complete",
                    RenewPeriodDays = cast.Repeat?.EveryDays,
                    RenewMaxCount = cast.Repeat?.Times,
                    RenewCurrentCount = cast.Repeat?.Completed ?? 0,
                    Created = set,
                    Modified = modified,
                    Guid = cast.Guid,
                    IsCast = true
                } );
            }

            return planned;
        }

        /// <summary>
        /// A reminder a staff member set about a generated person, the day they wrote a note on them.
        /// </summary>
        private List<PlannedReminder> PlanPersonReminders( RemindersConfiguration config, Dictionary<int, LoadedPerson> byPersonId, HashSet<int> castPersonIds )
        {
            var rules = config.Generated.Person;
            var typePercents = ( _source.Notes?.Types ?? new List<NoteTypeDefinition>() )
                .Where( t => rules.PercentOfNotesByType.ContainsKey( t.Key ) )
                .ToDictionary( t => t.Guid, t => rules.PercentOfNotesByType[t.Key] / 100 );
            var notes = _db.Query( @"
SELECT [n].[EntityId], [n].[CreatedByPersonAliasId], [n].[CreatedDateTime], [n].[Guid], [nt].[Guid] AS [TypeGuid]
FROM [Note] AS [n]
INNER JOIN [NoteType] AS [nt] ON [nt].[Id] = [n].[NoteTypeId]
WHERE [n].[ForeignKey] = @ForeignKey AND [n].[ParentNoteId] IS NULL AND [n].[CreatedByPersonAliasId] IS NOT NULL AND [n].[CreatedDateTime] >= @Since
    AND [nt].[EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Guid] = @PersonEntityType )
ORDER BY [n].[Id]",
                P( "@ForeignKey", _foreignKey ), P( "@Since", _dates.Anchor.Date.AddDays( -rules.NoteWithinDays ) ), P( "@PersonEntityType", PersonEntityTypeGuid ) );

            var planned = new List<PlannedReminder>();
            foreach ( var note in notes )
            {
                var personId = ( int ) note["EntityId"];
                if ( castPersonIds.Contains( personId ) || !byPersonId.TryGetValue( personId, out var about ) || !about.CanAttend
                    || !typePercents.TryGetValue( ( Guid ) note["TypeGuid"], out var chance ) )
                {
                    continue;
                }

                var random = new SeededRandom( $"{_seed}:reminder:note:{note["Guid"]}" );
                if ( !random.Chance( chance ) )
                {
                    continue;
                }

                var set = ( ( DateTime ) note["CreatedDateTime"] ).AddMinutes( random.Between( 1, 3 ) );
                var firstDue = set.Date.AddDays( random.Between( rules.DaysAfter.Min, rules.DaysAfter.Max ) );
                var repeatDays = random.Chance( rules.RepeatPercent / 100 ) ? random.Pick( rules.RepeatEveryDays ) : ( int? ) null;
                var reminder = Live( config.Generated, random, set, firstDue, repeatDays );
                reminder.OwnerAliasId = ( int ) note["CreatedByPersonAliasId"];
                reminder.EntityId = about.AliasId;
                reminder.Note = random.Pick( config.Notes.Person ).Replace( "{name}", about.Cast.NickName ?? about.Cast.FirstName );
                reminder.Guid = SeededRandom.StableGuid( $"{_seed}:reminder:note:{note["Guid"]}" );
                planned.Add( reminder );
            }

            return planned;
        }

        /// <summary>
        /// A reminder a generated small group's leader set about their group.
        /// </summary>
        private List<PlannedReminder> PlanGroupReminders( RemindersConfiguration config, Dictionary<int, LoadedPerson> byPersonId, HashSet<int> castPersonIds, int smallGroupTypeId )
        {
            var rules = config.Generated.Group;
            var groups = _db.Query( @"
SELECT [g].[Id], [g].[Guid], [leader].[PersonId], [leader].[DateTimeAdded]
FROM [Group] AS [g]
CROSS APPLY (
    SELECT TOP 1 [gm].[PersonId], [gm].[DateTimeAdded]
    FROM [GroupMember] AS [gm]
    INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [gm].[GroupRoleId]
    WHERE [gm].[GroupId] = [g].[Id] AND [r].[IsLeader] = 1 AND [gm].[GroupMemberStatus] = 1 AND [gm].[IsArchived] = 0
    ORDER BY [gm].[Id] ) AS [leader]
WHERE [g].[ForeignKey] = @ForeignKey AND [g].[GroupTypeId] = @GroupTypeId AND [g].[IsActive] = 1 AND [g].[IsArchived] = 0
ORDER BY [g].[Id]",
                P( "@ForeignKey", _foreignKey ), P( "@GroupTypeId", smallGroupTypeId ) );

            var planned = new List<PlannedReminder>();
            var anchor = _dates.Anchor.Date;
            foreach ( var group in groups )
            {
                var leaderId = ( int ) group["PersonId"];
                if ( castPersonIds.Contains( leaderId ) || !byPersonId.TryGetValue( leaderId, out var leader ) )
                {
                    continue;
                }

                var random = new SeededRandom( $"{_seed}:reminder:group:{group["Guid"]}" );
                if ( !random.Chance( rules.PercentOfGroups / 100 ) )
                {
                    continue;
                }

                // The leader sets it while leading, on a day before the install day.
                var earliest = new[] { anchor.AddDays( -rules.SetWithinDays ), ( ( DateTime ) group["DateTimeAdded"] ).Date.AddDays( 1 ) }.Max();
                var span = ( int ) ( anchor.AddDays( -1 ) - earliest ).TotalDays;
                if ( span < 0 )
                {
                    continue;
                }

                var set = earliest.AddDays( random.Between( 0, span ) ).Add( DayTime( random ) );
                var firstDue = set.Date.AddDays( random.Between( rules.DaysAfter.Min, rules.DaysAfter.Max ) );
                var repeatDays = random.Chance( rules.RepeatPercent / 100 ) ? random.Pick( rules.RepeatEveryDays ) : ( int? ) null;
                var reminder = Live( config.Generated, random, set, firstDue, repeatDays );
                reminder.IsGroup = true;
                reminder.OwnerAliasId = leader.AliasId;
                reminder.EntityId = ( int ) group["Id"];
                reminder.Note = random.Pick( config.Notes.Group );
                reminder.Guid = SeededRandom.StableGuid( $"{_seed}:reminder:group:{group["Guid"]}" );
                planned.Add( reminder );
            }

            return planned;
        }

        /// <summary>
        /// Lives one generated reminder from the day it was set to the install day, as its owner completes it in Rock. Each
        /// time it comes due, the owner completes it a few days late, unless it came due lately and is one of the ones still
        /// due. Completing a repeating reminder moves it to the completion time plus its period, until it has repeated as
        /// many times as it may.
        /// </summary>
        private PlannedReminder Live( GeneratedReminders rules, SeededRandom random, DateTime set, DateTime firstDue, int? repeatDays )
        {
            var anchor = _dates.Anchor.Date;
            var times = repeatDays.HasValue ? random.Pick( rules.RepeatTimes ) : null;
            var reminder = new PlannedReminder
            {
                Created = set,
                Modified = set,
                ReminderDate = firstDue,
                RenewPeriodDays = repeatDays,
                RenewMaxCount = times
            };

            while ( reminder.ReminderDate.Date <= anchor )
            {
                var due = reminder.ReminderDate;
                var completed = due.Date.AddDays( random.Between( rules.CompletedDaysLate.Min, rules.CompletedDaysLate.Max ) ).Add( DayTime( random ) );
                var isRecent = due.Date >= anchor.AddDays( -rules.DueWithinDays );
                if ( completed.Date >= anchor || ( isRecent && random.Chance( rules.DuePercent / 100 ) ) )
                {
                    return reminder;
                }

                if ( !repeatDays.HasValue || ( times.HasValue && reminder.RenewCurrentCount >= times.Value ) )
                {
                    reminder.IsComplete = true;
                    reminder.Modified = completed;
                    return reminder;
                }

                // A next date on the install day would come due partway through it, after the loader counted. Completing a
                // day later moves it to tomorrow.
                if ( completed.AddDays( repeatDays.Value ).Date == anchor )
                {
                    completed = completed.AddDays( 1 );
                }

                reminder.RenewCurrentCount++;
                reminder.ReminderDate = completed.AddDays( repeatDays.Value );
                reminder.Modified = completed;
            }

            return reminder;
        }

        /// <summary>
        /// A Cast reminder's day, at midnight.
        /// </summary>
        private DateTime Resolve( ReminderDay day )
        {
            var anchor = _dates.Anchor.Date;
            if ( day.DaysAgo.HasValue )
            {
                return anchor.AddDays( -day.DaysAgo.Value );
            }

            if ( day.DaysAhead.HasValue )
            {
                return anchor.AddDays( day.DaysAhead.Value );
            }

            if ( day.BeforeNextBaptismSunday.HasValue )
            {
                var baptism = _source.Steps?.StepTypes.SingleOrDefault( t => t.Source?.Kind == "baptism" )?.Source
                    ?? throw new InvalidDataException( "configuration/reminders.json: beforeNextBaptismSunday needs the Baptism step in configuration/steps.json." );
                var sunday = Enumerable.Range( anchor.Year, 2 ).SelectMany( y => new[] { SundayCalendar.Easter( y ), baptism.SummerSunday.In( y ) } ).Where( d => d > anchor ).Min();
                var date = sunday.AddDays( -day.BeforeNextBaptismSunday.Value );
                return date > anchor ? date : anchor.AddDays( 1 );
            }

            throw new InvalidDataException( "configuration/reminders.json: a Cast reminder day needs daysAgo, daysAhead, or beforeNextBaptismSunday." );
        }

        /// <summary>
        /// A time in the working day, to the minute.
        /// </summary>
        private static TimeSpan DayTime( SeededRandom random ) => TimeSpan.FromMinutes( random.Between( 8 * 60, 17 * 60 + 30 ) );

        #endregion

        #region Writing

        private void Write( RemindersConfiguration config, List<PlannedReminder> reminders )
        {
            var personTypeId = _db.RequireIdByGuid( "ReminderType", config.PersonReminderType );
            var groupTypeId = _db.RequireIdByGuid( "ReminderType", config.GroupReminderType );
            var table = TableFactory.Create( "ReminderTypeId:int", "PersonAliasId:int", "EntityId:int", "Note:string", "ReminderDate:date", "IsComplete:bool", "RenewPeriodDays:int",
                "RenewMaxCount:int", "RenewCurrentCount:int", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "Guid:guid", "ForeignKey:string" );
            foreach ( var r in reminders )
            {
                table.Rows.Add( r.IsGroup ? groupTypeId : personTypeId, r.OwnerAliasId, r.EntityId, r.Note, r.ReminderDate, r.IsComplete, Value( r.RenewPeriodDays ), Value( r.RenewMaxCount ),
                    r.RenewCurrentCount, r.Created, r.Modified, r.OwnerAliasId, r.OwnerAliasId, r.Guid, _foreignKey );
            }

            _db.BulkInsert( "Reminder", table, keepIdentity: false );
        }

        /// <summary>
        /// Sets each owner's ReminderCount as Rock's Process Reminders job does: their reminders of an active type that are
        /// not complete and due by now, which on the install day is by midnight. An owner with none due gets 0 when one of
        /// their reminders has come due before, as Rock's save hook leaves them, and keeps no count otherwise. Returns how
        /// many people have reminders due.
        /// </summary>
        private int UpdateReminderCounts()
        {
            var tomorrow = _dates.Anchor.Date.AddDays( 1 );
            _db.Execute( @"
UPDATE [p]
SET [ReminderCount] = [c].[Due]
FROM [Person] AS [p]
INNER JOIN (
    SELECT [pa].[PersonId], SUM( CASE WHEN [rt].[IsActive] = 1 AND [r].[IsComplete] = 0 AND [r].[ReminderDate] < @Tomorrow THEN 1 ELSE 0 END ) AS [Due]
    FROM [Reminder] AS [r]
    INNER JOIN [ReminderType] AS [rt] ON [rt].[Id] = [r].[ReminderTypeId]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[PersonAliasId]
    WHERE [r].[ForeignKey] = @ForeignKey AND ( [r].[ReminderDate] < @Tomorrow OR [r].[IsComplete] = 1 OR [r].[RenewCurrentCount] > 0 )
    GROUP BY [pa].[PersonId] ) AS [c] ON [c].[PersonId] = [p].[Id]",
                P( "@ForeignKey", _foreignKey ), P( "@Tomorrow", tomorrow ) );

            return Convert.ToInt32( _db.Scalar( @"
SELECT COUNT( DISTINCT [pa].[PersonId] )
FROM [Reminder] AS [r]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [r].[PersonAliasId]
WHERE [r].[ForeignKey] = @ForeignKey AND [r].[IsComplete] = 0 AND [r].[ReminderDate] < @Tomorrow",
                P( "@ForeignKey", _foreignKey ), P( "@Tomorrow", tomorrow ) ) );
        }

        private static object Value( int? value ) => value.HasValue ? value.Value : DBNull.Value;

        #endregion
    }
}
