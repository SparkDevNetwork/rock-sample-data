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
        11/16/26 - CLAUDE

        Classes come from the Starting Point Class and Foundations steps, and leaders from the memberships the steps
        step read back, so assessments follow exactly what the steps part wrote, and they need it. Who is an active
        adult is the steps step's rule. Rock's names, profiles, and motivator keys are read from Rock's defined values,
        as Rock's services read them. No workflow rows are written: Rock's DISC Request and Request Assessment
        workflows stay unused, and the Assessment rows fill every screen.

        Reason: decisions D-assessments-1 and D-assessments-2.
    */

    /// <summary>
    /// Writes every planned assessment, its answers, and the person attribute values its result saved.
    /// </summary>
    internal sealed class AssessmentsLoader
    {
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );
        private static readonly Guid DefinedValueEntityTypeGuid = new Guid( "53D4BF38-C49E-4A52-8B0E-5E016FB9574E" );
        private static readonly Guid DiscResultsTypeGuid = new Guid( "F06DDAD8-6058-4182-AD0A-B523BB7A2D78" );
        private static readonly Guid SpiritualGiftsTypeGuid = new Guid( "9D9628F0-7FC5-411E-B9DF-740AA17689A0" );
        private static readonly Guid ConflictProfileTypeGuid = new Guid( "EE7E089E-DF81-4407-8BFA-AD865FA5427A" );
        private static readonly Guid MotivatorTypeGuid = new Guid( "1DFF1804-0055-491E-9559-54EA3F8F89D1" );
        private static readonly Guid MotivatorThemeTypeGuid = new Guid( "354715FA-564A-420A-8324-0411988AE7AB" );

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;
        private readonly DateTime _historyStart;

        public AssessmentsLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed, DateTime historyStart )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
            _historyStart = historyStart;
        }

        /// <summary>
        /// Plans and writes every assessment.
        /// </summary>
        /// <param name="steps">What the steps step read and planned.</param>
        /// <param name="campusPastors">Each campus's campus pastor.</param>
        public void Load( PeopleLoader people, HashSet<string> castHouseholdKeys, StepFacts steps, StepPlan stepPlan, Dictionary<string, LoadedPerson> campusPastors )
        {
            var config = _source.Assessments;
            if ( config == null )
            {
                _db.Log.Write( "  Skipped (this package has no configuration/assessments.json)" );
                return;
            }

            var instruments = AssessmentInstrument.All( ReadReference() );
            var facts = ReadFacts( config, people, steps, stepPlan, campusPastors );
            var plan = AssessmentPlanner.Plan( config, instruments, facts, _seed, _dates.Resolve( config.Adopted ).Value, _dates.Resolve );
            Write( config, plan );
            Report( config, plan, facts );
        }

        #region Reading

        private AssessmentReference ReadReference()
        {
            var reference = new AssessmentReference();
            foreach ( var row in DefinedValues( DiscResultsTypeGuid ) )
            {
                reference.DiscProfiles[( string ) row["Value"]] = ( Guid ) row["Guid"];
            }

            foreach ( var type in new[] { SpiritualGiftsTypeGuid, ConflictProfileTypeGuid, MotivatorTypeGuid, MotivatorThemeTypeGuid } )
            {
                foreach ( var row in DefinedValues( type ) )
                {
                    reference.Names[( Guid ) row["Guid"]] = ( string ) row["Value"];
                }
            }

            // Each motivator and theme's AttributeScoreKey, and each motivator's Theme, from their defined value attributes.
            var definedValueEntityTypeId = _db.RequireIdByGuid( "EntityType", DefinedValueEntityTypeGuid );
            foreach ( var row in _db.Query( @"
SELECT [dv].[Guid], [a].[Key], [av].[Value]
FROM [DefinedValue] AS [dv]
INNER JOIN [DefinedType] AS [dt] ON [dt].[Id] = [dv].[DefinedTypeId]
INNER JOIN [Attribute] AS [a] ON [a].[EntityTypeId] = @EntityTypeId AND [a].[EntityTypeQualifierColumn] = 'DefinedTypeId' AND [a].[EntityTypeQualifierValue] = CAST( [dt].[Id] AS NVARCHAR( 200 ) )
INNER JOIN [AttributeValue] AS [av] ON [av].[AttributeId] = [a].[Id] AND [av].[EntityId] = [dv].[Id]
WHERE [dt].[Guid] IN ( @Motivators, @Themes ) AND [a].[Key] IN ( 'AttributeScoreKey', 'Theme' )",
                P( "@EntityTypeId", definedValueEntityTypeId ), P( "@Motivators", MotivatorTypeGuid ), P( "@Themes", MotivatorThemeTypeGuid ) ) )
            {
                var value = row["Value"] as string;
                if ( string.IsNullOrWhiteSpace( value ) )
                {
                    continue;
                }

                if ( ( string ) row["Key"] == "Theme" )
                {
                    reference.Themes[( Guid ) row["Guid"]] = Guid.Parse( value );
                }
                else
                {
                    reference.ScoreKeys[( Guid ) row["Guid"]] = value;
                }
            }

            return reference;
        }

        private List<Dictionary<string, object>> DefinedValues( Guid definedTypeGuid )
        {
            return _db.Query( @"
SELECT [dv].[Value], [dv].[Guid]
FROM [DefinedValue] AS [dv]
INNER JOIN [DefinedType] AS [dt] ON [dt].[Id] = [dv].[DefinedTypeId]
WHERE [dt].[Guid] = @Guid", P( "@Guid", definedTypeGuid ) );
        }

        private AssessmentFacts ReadFacts( AssessmentsConfiguration config, PeopleLoader people, StepFacts steps, StepPlan stepPlan, Dictionary<string, LoadedPerson> campusPastors )
        {
            var loaded = people.Households.SelectMany( h => h.Members ).GroupBy( m => m.PersonId ).ToDictionary( g => g.Key, g => g.First() );
            var facts = new AssessmentFacts
            {
                Anchor = _dates.Anchor,
                HistoryStart = _historyStart,
                CampusPastorAliases = campusPastors.ToDictionary( p => p.Key, p => p.Value.AliasId )
            };

            foreach ( var person in steps.People )
            {
                var member = loaded[person.PersonId];
                if ( !member.IsAdult || member.Cast.RecordType == "business" )
                {
                    continue;
                }

                facts.People.Add( new AssessmentPerson
                {
                    PersonId = person.PersonId,
                    AliasId = person.AliasId,
                    Guid = person.Guid,
                    Name = person.Name,
                    IsCast = person.IsCast,
                    CampusKey = person.CampusKey,
                    AdultSince = AdultSince( member ),
                    IsActiveAdult = person.IsActiveAdult,
                    FirstVisit = person.FirstVisit,
                    Cast = person.IsCast ? member.Cast.Assessments ?? new Dictionary<string, CastAssessment>() : new Dictionary<string, CastAssessment>()
                } );
            }

            var adults = facts.People.Select( p => p.PersonId ).ToHashSet();
            var classTypes = config.Classes.Select( c => c.StepType ).ToHashSet();
            facts.Classes = stepPlan.Steps.Where( s => classTypes.Contains( s.StepType ) && adults.Contains( s.PersonId ) )
                .Select( s => new AssessmentClass( s.PersonId, s.StepType, s.Start ) )
                .ToList();
            facts.Leaders = steps.Memberships.Where( m => m.IsLeader && !m.IsPending && m.Left == null && adults.Contains( m.PersonId ) )
                .Select( m => new AssessmentLeader( m.PersonId, m.GroupType, m.Joined ) )
                .ToList();

            // Staff who load: a campus pastor whose campus is not in the dataset is not loaded.
            var staff = _source.Staff?.Staff ?? new List<StaffMember>();
            var staffByKey = staff.ToDictionary( s => s.Key );
            foreach ( var member in staff )
            {
                if ( people.People.TryGetValue( member.Person, out var person ) )
                {
                    facts.StaffAliases[member.Key] = person.AliasId;
                }
            }

            foreach ( var member in staff.Where( s => people.People.ContainsKey( s.Person ) && adults.Contains( people.People[s.Person].PersonId ) ) )
            {
                int? supervisor = member.Supervisor != null && staffByKey.TryGetValue( member.Supervisor, out var boss ) && people.People.TryGetValue( boss.Person, out var bossPerson ) ? bossPerson.AliasId : null;
                facts.Staff.Add( new AssessmentStaff( people.People[member.Person].PersonId, _dates.Resolve( member.StartDate ).Value, supervisor ) );
            }

            return facts;
        }

        /// <summary>
        /// The first day the person is 18 and out of high school, the day after Rock's grade transition in their graduation year.
        /// </summary>
        private static DateTime AdultSince( LoadedPerson person )
        {
            var byAge = person.BirthDate?.AddYears( 18 ) ?? DateTime.MinValue;
            var graduated = person.GraduationYear.HasValue ? new DateTime( person.GraduationYear.Value, AnchorDates.GradeTransitionMonth, AnchorDates.GradeTransitionDay ).AddDays( 1 ) : DateTime.MinValue;
            return byAge > graduated ? byAge : graduated;
        }

        #endregion

        #region Writing

        private void Write( AssessmentsConfiguration config, AssessmentPlan plan )
        {
            var typeIds = config.Types.ToDictionary( t => t.Key, t => _db.RequireIdByGuid( "AssessmentType", t.Guid ) );
            var nextId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [Assessment]" ) );
            var rows = TableFactory.Create( "Id:int", "PersonAliasId:int", "AssessmentTypeId:int", "RequesterPersonAliasId:int", "RequestedDateTime:date", "RequestedDueDate:date", "Status:int",
                "CompletedDateTime:date", "AssessmentResultData:string", "LastReminderDate:date", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int",
                "Guid:guid", "ForeignKey:string" );
            foreach ( var a in plan.Assessments )
            {
                // A request is made by its requester, and a result is saved by the person who took it.
                var createdBy = a.RequesterAliasId ?? a.AliasId;
                rows.Rows.Add( nextId++, a.AliasId, typeIds[a.Type], Value( a.RequesterAliasId ), Value( a.Requested ), Value( a.Due ), a.IsPending ? 0 : 1,
                    Value( a.Completed ), Value( a.Answers == null ? null : a.Score.ResultData( a.SecondsToTake ) ), Value( a.LastReminder ),
                    a.Created, a.Completed ?? a.Created, createdBy, a.IsPending ? createdBy : a.AliasId, a.Guid, _foreignKey );
            }

            var personEntityTypeId = _db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            var attributeIds = _db.Query( @"
SELECT [Id], [Key]
FROM [Attribute]
WHERE [EntityTypeId] = @EntityTypeId AND ISNULL( [EntityTypeQualifierColumn], '' ) = ''", P( "@EntityTypeId", personEntityTypeId ) )
                .GroupBy( r => ( string ) r["Key"] )
                .ToDictionary( g => g.Key, g => ( int ) g.First()["Id"] );
            var values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "IsPersistedValueDirty:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            foreach ( var value in plan.Values )
            {
                if ( !attributeIds.TryGetValue( value.AttributeKey, out var attributeId ) )
                {
                    throw new InvalidOperationException( $"Rock has no {value.AttributeKey} person attribute, which its assessments save." );
                }

                values.Rows.Add( false, attributeId, value.PersonId, value.Value, true, SeededRandom.StableGuid( $"{_seed}:assessments:value:{value.PersonId}:{value.AttributeKey}" ), _foreignKey, value.Created, value.Modified );
            }

            _db.BulkInsert( "Assessment", rows, keepIdentity: true );
            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );
        }

        private static object Value( object value ) => value ?? DBNull.Value;

        #endregion

        private void Report( AssessmentsConfiguration config, AssessmentPlan plan, AssessmentFacts facts )
        {
            var all = plan.Assessments;
            var byType = string.Join( ", ", config.Types.Select( t =>
            {
                var rows = all.Where( a => a.Type == t.Key ).ToList();
                return $"{t.Key} {rows.Count( r => !r.IsPending ):N0} ({rows.Count( r => r.IsPending ):N0} pending)";
            } ) );
            var bySource = string.Join( ", ", all.GroupBy( a => a.Source ).OrderBy( g => g.Key, StringComparer.Ordinal ).Select( g => $"{g.Key} {g.Count():N0}" ) );
            var people = all.Select( a => a.PersonId ).Distinct().Count();
            _db.Log.Write( $"  Assessments: {all.Count:N0} for {people:N0} of {facts.People.Count:N0} adults: {byType}" );
            _db.Log.Write( $"  By rule: {bySource}; {all.Count( a => a.LastReminder.HasValue ):N0} reminded; {plan.Values.Count:N0} person attribute values" );
            var profiles = all.Where( a => a.Type == "disc" && a.Score != null ).GroupBy( a => a.Score.Summary ).OrderByDescending( g => g.Count() ).Take( 6 );
            _db.Log.Write( $"  Most common DISC profiles: {string.Join( ", ", profiles.Select( g => $"{g.Key} {g.Count():N0}" ) )}" );
        }
    }
}
