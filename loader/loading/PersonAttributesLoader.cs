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

using System.Data;
using System.Text.Json;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/16/26 - CLAUDE

        Rock's Extended Attributes tab, check-in, and data views read person attributes Rock ships, and the sample
        data left most of them empty. This step fills them from configuration/person-attributes.json:

        1. The Cast's own values (Employer, Position, Legal Notes, Do Not Send Giving Statement), as written in
           their files. The Cast gets no drawn employer, school, note, or visit: its stories are hand-written.
        2. Drawn values for generated people only: Employer and Position, School, Legal Notes, Source of Visit and
           Previous Church, and the Allergy of a kid who never answered a registration form.
        3. Values worked out from records, for everyone: Ability Level from the room a young kid checks in to,
           Date Engaged from the first small group, serving team, or gift, and an Envelope Number for a share of
           check writers.

        Each drawn value comes from its own stream, seeded by the person or household, so no other row changes.
        The step runs after registrations, because a kid's allergy from a registration form comes first and the
        same draw fills the rest, and after giving, because Date Engaged and envelopes read the gifts written.
        Every value gets Rock's derived columns from the attribute value hook at the end of the load.

        Reason: decisions 250 to 258.
    */

    /// <summary>
    /// The person attributes Rock ships that the sample data fills, and the Date Engaged attribute it adds.
    /// </summary>
    internal sealed class PersonAttributesLoader
    {
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );
        private static readonly Guid DateFieldTypeGuid = new Guid( "6B6AA175-4758-453F-8D83-FCD8044B5F36" );
        private static readonly Guid ContributionTypeGuid = new Guid( "2D607262-52D6-4724-910D-5C6E8FB89ACC" );
        private static readonly Guid PersonRecordTypeGuid = new Guid( "36CF10D6-C695-413D-8E7C-4546EFEF385E" );

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly PersonAttributesConfiguration _config;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;
        private readonly Dictionary<string, int> _attributeIds = new Dictionary<string, int>( StringComparer.Ordinal );
        private readonly DataTable _values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "IsPersistedValueDirty:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>( StringComparer.Ordinal );

        public PersonAttributesLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _config = source.PersonAttributes;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// True when the package has configuration/person-attributes.json. A package from before it has none, and nothing loads.
        /// </summary>
        public bool HasConfiguration => _config != null;

        #region Setup

        /// <summary>
        /// Checks that Rock has every person attribute the file lists, adds Date Engaged to Rock's Membership category,
        /// and adds the schools of the dataset's campuses to Rock's School defined type. Returns a summary for the log.
        /// </summary>
        public string LoadSetup( IEnumerable<string> campusKeys )
        {
            foreach ( var attribute in _config.RockAttributes )
            {
                var row = _db.Query( "SELECT [Id], [Key] FROM [Attribute] WHERE [Guid] = @Guid", P( "@Guid", attribute.Guid ) ).SingleOrDefault();
                if ( row == null || ( string ) row["Key"] != attribute.Key )
                {
                    throw new InvalidDataException( $"configuration/person-attributes.json lists the person attribute {attribute.Key} ({attribute.Guid}), which this Rock does not have under that key." );
                }

                _attributeIds[attribute.Key] = Convert.ToInt32( row["Id"] );
            }

            var dateEngaged = _config.DateEngaged;
            _attributeIds[dateEngaged.Key] = InsertPersonAttribute( _db, DateFieldTypeGuid, dateEngaged.Key, dateEngaged.Name, dateEngaged.Description, dateEngaged.Order,
                isPublic: false, dateEngaged.Guid, dateEngaged.Qualifiers, _db.RequireIdByGuid( "Category", dateEngaged.Category ), _foreignKey, DateTime.Now );

            var schools = _config.Schools;
            var definedTypeId = _db.RequireIdByGuid( "DefinedType", schools.DefinedType );
            var order = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Order] ), -1 ) + 1 FROM [DefinedValue] WHERE [DefinedTypeId] = @Id", P( "@Id", definedTypeId ) ) );
            var campusNames = _source.Campuses.Campuses.ToDictionary( c => c.Key, c => c.Name );
            var added = 0;
            foreach ( var campusKey in campusKeys.Where( schools.Campuses.ContainsKey ) )
            {
                foreach ( var school in schools.Campuses[campusKey] )
                {
                    var band = Band( school.Band );
                    var description = $"A school near the {campusNames[campusKey]} campus, {GradeName( band.MinGrade )} to {GradeName( band.MaxGrade )}.";
                    ConfigurationLoader.UpsertDefinedValue( _db, _foreignKey, definedTypeId, school.Guid, school.Name, description, order++ );
                    added++;
                }
            }

            return $"{_config.RockAttributes.Count} of Rock's attributes found, {dateEngaged.Name} added, and {added} schools";
        }

        /// <summary>
        /// Adds a person attribute in a category, with its settings, as Rock's attribute editor saves one. Returns its Id.
        /// </summary>
        internal static int InsertPersonAttribute( RockDatabase db, Guid fieldTypeGuid, string key, string name, string description, int order, bool isPublic, Guid guid,
            IReadOnlyDictionary<string, string> qualifiers, int categoryId, string foreignKey, DateTime now )
        {
            var personEntityTypeId = db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            var id = Convert.ToInt32( db.Scalar( @"
INSERT INTO [Attribute] ( [IsSystem], [FieldTypeId], [EntityTypeId], [EntityTypeQualifierColumn], [EntityTypeQualifierValue], [Key], [Name], [Description], [Order], [IsGridColumn], [DefaultValue], [IsMultiValue], [IsRequired],
    [AllowSearch], [IsIndexEnabled], [IsAnalytic], [IsAnalyticHistory], [IsActive], [EnableHistory], [ShowOnBulk], [IsPublic], [IsDefaultPersistedValueDirty], [IsSuppressHistoryLogging], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, ( SELECT [Id] FROM [FieldType] WHERE [Guid] = @FieldType ), @EntityTypeId, '', '', @Key, @Name, @Description, @Order, 0, '', 0, 0,
    0, 0, 0, 0, 1, 0, 0, @IsPublic, 1, 0, @Guid, @ForeignKey, @Now, @Now )",
                P( "@FieldType", fieldTypeGuid ), P( "@EntityTypeId", personEntityTypeId ), P( "@Key", key ), P( "@Name", name ),
                P( "@Description", description ), P( "@Order", order ), P( "@IsPublic", isPublic ), P( "@Guid", guid ),
                P( "@ForeignKey", foreignKey ), P( "@Now", now ) ) );

            foreach ( var qualifier in qualifiers )
            {
                db.Execute( "INSERT INTO [AttributeQualifier] ( [IsSystem], [AttributeId], [Key], [Value], [Guid], [ForeignKey] ) VALUES ( 0, @AttributeId, @Key, @Value, NEWID(), @ForeignKey )",
                    P( "@AttributeId", id ), P( "@Key", qualifier.Key ), P( "@Value", qualifier.Value ), P( "@ForeignKey", foreignKey ) );
            }

            db.Execute( "INSERT INTO [AttributeCategory] ( [AttributeId], [CategoryId] ) VALUES ( @AttributeId, @CategoryId )", P( "@AttributeId", id ), P( "@CategoryId", categoryId ) );
            return id;
        }

        private SchoolBand Band( string key )
        {
            return _config.Schools.Bands.FirstOrDefault( b => b.Key == key )
                ?? throw new InvalidDataException( $"configuration/person-attributes.json: a school names grade band '{key}', which 'bands' does not list." );
        }

        private static string GradeName( int grade )
        {
            return grade == 0 ? "kindergarten" : $"grade {grade}";
        }

        #endregion Setup

        #region Values

        /// <summary>
        /// What the values step reads from the steps before it.
        /// </summary>
        internal sealed class Inputs
        {
            public PeopleLoader People { get; set; }

            public HashSet<string> CastHouseholdKeys { get; set; }

            public AttendanceLoader Attendance { get; set; }

            public Dictionary<string, int> CheckInGroupIds { get; set; }

            /// <summary>
            /// The last Sunday of the history window: the room a kid checks in to now is the room they had that day.
            /// </summary>
            public DateTime LastSunday { get; set; }

            public DateTime HistoryStart { get; set; }

            /// <summary>
            /// The check gifts the giving step wrote, or null when giving did not load.
            /// </summary>
            public IReadOnlyList<CheckGift> CheckGifts { get; set; }

            public int SmallGroupTypeId { get; set; }

            public int ServingTypeId { get; set; }
        }

        /// <summary>
        /// Writes every value: the Cast's, the drawn ones, Date Engaged, and envelopes. Returns a summary for the log.
        /// </summary>
        public string LoadValues( Inputs inputs )
        {
            var households = inputs.People.Households;
            var generated = households.Where( h => !inputs.CastHouseholdKeys.Contains( h.Cast.Key ) && h.Cast.Kind != "business" ).ToList();
            var cast = households.Where( h => inputs.CastHouseholdKeys.Contains( h.Cast.Key ) ).ToList();
            var businessNames = households.Where( h => h.Cast.Kind == "business" ).SelectMany( h => h.Cast.Members ).ToDictionary( m => m.Guid, m => m.LastName );

            AddCastValues( cast );
            AddEmployment( generated, businessNames );
            AddSchools( generated );
            AddLegalNotes( generated );
            AddAbilityLevels( households.Where( h => h.Cast.Kind != "business" ).ToList(), inputs );
            AddVisits( generated, inputs.HistoryStart );
            AddAllergies( generated );
            var withoutRecords = AddDateEngaged( inputs );
            if ( inputs.CheckGifts != null )
            {
                AddEnvelopes( inputs.CheckGifts );
            }

            _db.BulkInsert( "AttributeValue", _values, keepIdentity: false );
            var parts = _counts.Where( c => c.Value > 0 ).Select( c => $"{c.Key} {c.Value:N0}" );
            return $"{string.Join( ", ", parts )} ({withoutRecords:N0} Engaged or Leading adults with no group, team, or gift)";
        }

        /// <summary>
        /// Adds one value, with the value Guid every writer of a person attribute uses, so a row is the same whichever step writes it.
        /// </summary>
        private void Add( string key, LoadedPerson person, string value, string countAs = null )
        {
            var attributeGuid = key == _config.DateEngaged.Key ? _config.DateEngaged.Guid : _config.AttributeGuid( key );
            _values.Rows.Add( false, _attributeIds[key], person.PersonId, value, true,
                SeededRandom.StableGuid( $"{person.Cast.Guid}:person-attribute:{attributeGuid}" ), _foreignKey, _dates.Anchor, _dates.Anchor );
            countAs ??= key;
            _counts[countAs] = ( _counts.TryGetValue( countAs, out var count ) ? count : 0 ) + 1;
        }

        private static bool CanHaveValues( LoadedPerson person )
        {
            return person.CanAttend && person.Cast.RecordType != "business";
        }

        /// <summary>
        /// The Cast's own values for the keys the file loads, as Rock stores each: text as written, and a yes-no as True or False.
        /// </summary>
        private void AddCastValues( List<LoadedHousehold> cast )
        {
            foreach ( var person in cast.SelectMany( h => h.Members ).Distinct().Where( p => p.Cast.Attributes?.Others != null ).OrderBy( p => p.PersonId ) )
            {
                foreach ( var key in _config.CastLoaded )
                {
                    if ( !person.Cast.Attributes.Others.TryGetValue( key, out var element ) )
                    {
                        continue;
                    }

                    var value = element.ValueKind switch
                    {
                        JsonValueKind.String => element.GetString().Trim(),
                        JsonValueKind.True => "True",
                        JsonValueKind.False => "False",
                        JsonValueKind.Number => element.GetRawText(),
                        _ => throw new InvalidDataException( $"Cast person {person.Cast.Guid}: attribute {key} must be text, a number, or true or false." )
                    };
                    if ( value.Length > 0 )
                    {
                        Add( key, person, value );
                    }
                }
            }
        }

        /*
            11/16/26 - CLAUDE

            A business contact works at their own business, as the Owner, or as a Co-owner when a married couple owns it
            together (decisions 63 to 65). Other working-age generated adults have an employer by the configured share,
            from invented employers. The Cast's employers are in its files.

            Reason: decision 253.
        */

        private void AddEmployment( List<LoadedHousehold> generated, Dictionary<Guid, string> businessNames )
        {
            var rules = _config.Employment;
            var contactsByBusiness = new Dictionary<Guid, int>();
            foreach ( var person in generated.SelectMany( h => h.Members ).Where( CanHaveValues ).Where( p => p.IsAdult ).OrderBy( p => p.PersonId ) )
            {
                var business = person.Cast.Relationships.FirstOrDefault( r => r.Type == "business" && businessNames.ContainsKey( r.With ) );
                if ( business != null )
                {
                    var place = contactsByBusiness.TryGetValue( business.With, out var count ) ? count : 0;
                    contactsByBusiness[business.With] = place + 1;
                    Add( "Employer", person, businessNames[business.With] );
                    Add( "Position", person, rules.ContactPositions[Math.Min( place, rules.ContactPositions.Count - 1 )] );
                    continue;
                }

                var age = person.BirthDate.HasValue ? _dates.AgeOn( person.BirthDate.Value ) : ( int? ) null;
                var random = new SeededRandom( $"{_seed}:person-attributes:employment:{person.Cast.Guid}" );
                if ( age < rules.MinAge || age > rules.MaxAge || !age.HasValue || !random.Chance( rules.Percent / 100 ) )
                {
                    continue;
                }

                var employer = random.Pick( rules.Employers );
                Add( "Employer", person, employer.Name );
                Add( "Position", person, random.Pick( employer.Positions ) );
            }
        }

        /// <summary>
        /// A kid's or student's grade on the install day, or null when they are not in school.
        /// </summary>
        private int? GradeNow( LoadedPerson person )
        {
            if ( person.IsAdult || !person.GraduationYear.HasValue )
            {
                return null;
            }

            var grade = AnchorDates.GradeOn( person.GraduationYear.Value, _dates.Anchor );
            return grade >= 0 && grade <= 12 ? grade : null;
        }

        /*
            11/16/26 - CLAUDE

            A parent fills in School for all their kids or none, so the draw is by household, and kids in one grade band
            go to the same school. Each campus's schools are invented and checked against real Arizona schools.

            Reason: decision 252.
        */

        private void AddSchools( List<LoadedHousehold> generated )
        {
            var rules = _config.Schools;
            foreach ( var household in generated.Where( h => rules.Campuses.ContainsKey( h.CampusKey ) ) )
            {
                var students = household.Members.Where( CanHaveValues ).Select( p => (Person: p, Grade: GradeNow( p )) ).Where( s => s.Grade.HasValue ).ToList();
                if ( students.Count == 0 || !new SeededRandom( $"{_seed}:person-attributes:school:{household.Cast.Guid}" ).Chance( rules.HouseholdPercent / 100 ) )
                {
                    continue;
                }

                foreach ( var (person, grade) in students )
                {
                    var band = rules.Bands.First( b => grade >= b.MinGrade && grade <= b.MaxGrade );
                    var schools = rules.Campuses[household.CampusKey].Where( s => s.Band == band.Key ).ToList();
                    var school = new SeededRandom( $"{_seed}:person-attributes:school:{household.Cast.Guid}:{band.Key}" ).Pick( schools );
                    Add( "School", person, school.Guid.ToString() );
                }
            }
        }

        /*
            11/16/26 - CLAUDE

            A pickup or custody note belongs to a kid whose parents live apart, so it goes to the kids who check in from a
            few single-parent households at each campus, every kid in the household with the same note. The note names
            the parent's role, never a person.

            Reason: decision 254.
        */

        private void AddLegalNotes( List<LoadedHousehold> generated )
        {
            var rules = _config.LegalNotes;
            foreach ( var campus in generated.GroupBy( h => h.CampusKey ) )
            {
                var chosen = campus
                    .Select( h => (Household: h, Adults: h.Members.Where( p => p.IsAdult && CanHaveValues( p ) ).ToList(), Kids: h.Members.Where( p => !p.IsAdult && CanHaveValues( p ) && AgeNow( p ) <= rules.MaxAge ).ToList()) )
                    .Where( c => c.Adults.Count == 1 && c.Kids.Count > 0 && rules.Notes.ContainsKey( c.Adults[0].Cast.Gender ) )
                    .OrderBy( c => SeededRandom.StableGuid( $"{_seed}:person-attributes:legal-notes:{c.Household.Cast.Guid}" ) )
                    .Take( rules.HouseholdsPerCampus );
                foreach ( var (household, adults, kids) in chosen )
                {
                    var note = new SeededRandom( $"{_seed}:person-attributes:legal-note:{household.Cast.Guid}" ).Pick( rules.Notes[adults[0].Cast.Gender] );
                    foreach ( var kid in kids )
                    {
                        Add( "LegalNotes", kid, note );
                    }
                }
            }
        }

        private int? AgeNow( LoadedPerson person )
        {
            return person.BirthDate.HasValue ? _dates.AgeOn( person.BirthDate.Value ) : null;
        }

        /*
            11/16/26 - CLAUDE

            Rock's three nursery and preschool check-in groups each filter by an Ability Level (Infant, Crawling or
            Walking, Potty Trained), and Rock leaves a kid with an ability level only the groups that match it. So a kid's
            ability level is the one of the room they check in to: their latest check-in was there, and on the last
            Sunday their age still puts them there. The level comes from Rock's own group, so it always fits. The Cast's
            toddlers follow the same rule: the level only names the room they already use, and Core 1 shows it.

            Reason: decision 255.
        */

        private void AddAbilityLevels( List<LoadedHousehold> households, Inputs inputs )
        {
            var rules = _config.AbilityLevel;
            var groupEntityTypeId = Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Group'" ) );
            var levels = new Dictionary<string, string>( StringComparer.Ordinal );
            foreach ( var key in rules.Groups )
            {
                var level = _db.Scalar( @"
SELECT TOP 1 [av].[Value]
FROM [AttributeValue] AS [av]
INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId]
WHERE [a].[EntityTypeId] = @GroupEntityTypeId AND [a].[Key] = 'AbilityLevel' AND [av].[EntityId] = @GroupId AND [av].[Value] <> ''",
                    P( "@GroupEntityTypeId", groupEntityTypeId ), P( "@GroupId", inputs.CheckInGroupIds[key] ) ) as string;
                if ( level != null )
                {
                    levels[key] = level.ToLowerInvariant();
                }
            }

            // Each kid's latest check-in, in any check-in room.
            var latest = new Dictionary<int, (DateTime Day, string Group)>();
            foreach ( var key in inputs.CheckInGroupIds.Keys )
            {
                foreach ( var day in inputs.Attendance.CheckInDays( new[] { key } ) )
                {
                    if ( !latest.TryGetValue( day.Key, out var seen ) || day.Value.Max > seen.Day )
                    {
                        latest[day.Key] = (day.Value.Max, key);
                    }
                }
            }

            foreach ( var kid in households.SelectMany( h => h.Members ).Distinct().Where( p => !p.IsAdult && CanHaveValues( p ) ).OrderBy( p => p.PersonId ) )
            {
                if ( !latest.TryGetValue( kid.PersonId, out var checkIn ) || !levels.TryGetValue( checkIn.Group, out var level )
                    || AttendanceLoader.CheckInGroupFor( kid, inputs.LastSunday ) != checkIn.Group
                    || !new SeededRandom( $"{_seed}:person-attributes:ability-level:{kid.Cast.Guid}" ).Chance( rules.Percent / 100 ) )
                {
                    continue;
                }

                Add( "AbilityLevel", kid, level );
            }
        }

        /*
            11/16/26 - CLAUDE

            A family fills out one connection card, so Source of Visit and Previous Church are drawn by household and
            every adult in it gets the same. Only households that first came inside the history window have them,
            since the card is a first-visit record. Previous Church describes a church and never names one.

            Reason: decision 256.
        */

        private void AddVisits( List<LoadedHousehold> generated, DateTime historyStart )
        {
            var rules = _config.Visit;
            foreach ( var household in generated )
            {
                var firstVisit = _dates.Resolve( household.Cast.Attendance?.Started );
                if ( !firstVisit.HasValue || firstVisit.Value < historyStart || firstVisit.Value > _dates.Anchor )
                {
                    continue;
                }

                var random = new SeededRandom( $"{_seed}:person-attributes:visit:{household.Cast.Guid}" );
                var source = random.Chance( rules.SourcePercent / 100 ) ? random.Pick( rules.Sources ) : null;
                var church = random.Chance( rules.PreviousChurchPercent / 100 ) ? random.Pick( rules.PreviousChurches ) : null;
                foreach ( var adult in household.Members.Where( p => p.IsAdult && CanHaveValues( p ) ) )
                {
                    if ( source != null )
                    {
                        Add( "SourceofVisit", adult, source.ToLowerInvariant() );
                    }

                    if ( church != null )
                    {
                        Add( "PreviousChurch", adult, church );
                    }
                }
            }
        }

        /*
            11/16/26 - CLAUDE

            Registrations save a kid's Allergy from the form, from the person's own draw (decision 211). A generated kid
            who never answered that form gets the same draw here, so the share is the same for every kid, and a kid who
            did answer keeps exactly what the form saved. Only kids: adults never get a drawn allergy.

            Reason: decision 251.
        */

        private void AddAllergies( List<LoadedHousehold> generated )
        {
            var allergyGuid = _config.AttributeGuid( "Allergy" );
            var fills = ( _source.Registrations?.Templates ?? new List<RegistrationTemplateDefinition>() )
                .SelectMany( t => t.Forms ).SelectMany( f => f.Fields )
                .Where( f => f.PersonAttribute == allergyGuid && f.Fill != null ).Select( f => f.Fill ).ToList();
            if ( fills.Count == 0 )
            {
                return;
            }

            var fill = fills[0];
            if ( fills.Any( f => f.Percent != fill.Percent || !f.Values.SequenceEqual( fill.Values ) || !f.Weights.SequenceEqual( fill.Weights ) ) )
            {
                throw new InvalidDataException( "configuration/registrations.json: every form that asks Rock's Allergy must fill it the same way, so a kid's allergy is the same on every form and in Rock." );
            }

            var saved = _db.Query( "SELECT [EntityId] FROM [AttributeValue] WHERE [AttributeId] = @AttributeId AND [EntityId] IS NOT NULL", P( "@AttributeId", _attributeIds["Allergy"] ) )
                .Select( r => Convert.ToInt32( r["EntityId"] ) ).ToHashSet();
            foreach ( var kid in generated.SelectMany( h => h.Members ).Where( p => !p.IsAdult && CanHaveValues( p ) ).OrderBy( p => p.PersonId ) )
            {
                if ( saved.Contains( kid.PersonId ) )
                {
                    continue;
                }

                var allergy = RegistrantPlanner.DrawPersonAnswer( _seed, allergyGuid, kid.Cast.Guid, fill );
                if ( allergy != null )
                {
                    Add( "Allergy", kid, allergy );
                }
            }
        }

        /*
            11/16/26 - CLAUDE

            Engaged means "in a group, serving, or giving" (configuration/connection-statuses.json), so Date Engaged is the
            day an Engaged or Leading adult first was: their first small group or serving team membership that was not
            pending, or the first contribution of their giving unit, read back from the rows the load wrote. An adult with
            none of those still carries the status, so they became engaged before the records the sample data keeps: a day
            from the years before the history window, and never before they turned 18.

            Reason: decision 257.
        */

        /// <summary>
        /// Writes Date Engaged for every Engaged or Leading adult. Returns how many had no group, team, or gift.
        /// </summary>
        private int AddDateEngaged( Inputs inputs )
        {
            var definition = _config.DateEngaged;
            var statuses = definition.Statuses.Select( key => _source.ConnectionStatuses.Statuses.Single( s => s.Key == key ).Guid ).ToList();
            var firsts = _db.Query( $@"
WITH [People] AS (
    SELECT [p].[Id], [p].[GivingId]
    FROM [Person] AS [p]
    INNER JOIN [DefinedValue] AS [status] ON [status].[Id] = [p].[ConnectionStatusValueId]
    WHERE {PeopleLoader.DatasetPersonFilter( "p" )}
        AND [p].[RecordTypeValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @PersonRecordType )
        AND [p].[AgeClassification] = 1
        AND [status].[Guid] IN ( {string.Join( ", ", statuses.Select( ( _, i ) => $"@Status{i}" ) )} )
),
[Firsts] AS (
    SELECT [gm].[PersonId], [gm].[DateTimeAdded] AS [First]
    FROM [GroupMember] AS [gm]
    INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
    WHERE [g].[GroupTypeId] IN ( @SmallGroupTypeId, @ServingTypeId ) AND [gm].[GroupMemberStatus] <> 2
    UNION ALL
    SELECT [p].[Id], [t].[TransactionDateTime]
    FROM [FinancialTransaction] AS [t]
    INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [t].[AuthorizedPersonAliasId]
    INNER JOIN [Person] AS [giver] ON [giver].[Id] = [pa].[PersonId]
    INNER JOIN [People] AS [p] ON [p].[GivingId] = [giver].[GivingId]
    WHERE [t].[TransactionTypeValueId] = ( SELECT [Id] FROM [DefinedValue] WHERE [Guid] = @ContributionType )
)
SELECT [p].[Id], MIN( [f].[First] ) AS [First]
FROM [People] AS [p]
LEFT JOIN [Firsts] AS [f] ON [f].[PersonId] = [p].[Id]
GROUP BY [p].[Id]",
                statuses.Select( ( g, i ) => P( $"@Status{i}", g ) )
                    .Concat( new[]
                    {
                        P( "@ForeignKey", _foreignKey ), P( "@PersonRecordType", PersonRecordTypeGuid ), P( "@ContributionType", ContributionTypeGuid ),
                        P( "@SmallGroupTypeId", inputs.SmallGroupTypeId ), P( "@ServingTypeId", inputs.ServingTypeId )
                    } ).ToArray() )
                .ToDictionary( r => Convert.ToInt32( r["Id"] ), r => r["First"] as DateTime? );

            var withoutRecords = 0;
            foreach ( var person in inputs.People.People.Values.Where( p => firsts.ContainsKey( p.PersonId ) ).OrderBy( p => p.PersonId ) )
            {
                var first = firsts[person.PersonId];
                if ( !first.HasValue )
                {
                    withoutRecords++;
                    first = BeforeRecords( person, inputs.HistoryStart, definition.YearsBeforeHistoryWithoutRecords );
                }

                Add( definition.Key, person, first.Value.Date.ToString( "o" ), "Date Engaged" );
            }

            return withoutRecords;
        }

        /// <summary>
        /// A day in the years before the history window, drawn by the person, and never before their 18th birthday. A
        /// person who turned 18 inside the window has their 18th birthday.
        /// </summary>
        private DateTime BeforeRecords( LoadedPerson person, DateTime historyStart, int years )
        {
            var earliest = historyStart.AddYears( -years );
            var adult = person.BirthDate?.AddYears( 18 ) ?? earliest;
            if ( adult > earliest )
            {
                earliest = adult;
            }

            if ( earliest >= historyStart )
            {
                return earliest;
            }

            var days = ( int ) ( historyStart - earliest ).TotalDays;
            return earliest.AddDays( new SeededRandom( $"{_seed}:person-attributes:date-engaged:{person.Cast.Guid}" ).Between( 0, days - 1 ) );
        }

        /*
            11/16/26 - CLAUDE

            A church gives offering envelopes to givers who ask for them, so a share of the people who wrote a check gift
            have an Envelope Number. Numbers are unique, counted up from the first number with small gaps, as a list kept
            for years has, in an order drawn from the seed.

            Reason: decision 258.
        */

        private void AddEnvelopes( IReadOnlyList<CheckGift> checkGifts )
        {
            var rules = _config.Envelopes;
            var writers = checkGifts.Select( c => c.Giver ).Where( CanHaveValues ).GroupBy( p => p.PersonId ).Select( g => g.First() )
                .Where( p => new SeededRandom( $"{_seed}:person-attributes:envelope:{p.Cast.Guid}" ).Chance( rules.Percent / 100 ) )
                .OrderBy( p => SeededRandom.StableGuid( $"{_seed}:person-attributes:envelope-order:{p.Cast.Guid}" ) )
                .ToList();
            var steps = new SeededRandom( $"{_seed}:person-attributes:envelope-numbers" );
            var number = rules.FirstNumber;
            foreach ( var writer in writers )
            {
                Add( "core_GivingEnvelopeNumber", writer, number.ToString( System.Globalization.CultureInfo.InvariantCulture ) );
                number += steps.Between( 1, rules.MaxStep );
            }
        }

        #endregion Values
    }
}
