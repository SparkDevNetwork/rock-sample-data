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

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/19/26 - CLAUDE

        Rock's Benevolence Request Detail block copies the requester onto the request when it saves: the legal first name,
        last name, email, home, cell, and work phones as Rock formats them, the home address's location, the connection
        status, and the campus. The case worker comes from RSR - Benevolence, the status is Pending, Approved, or Denied,
        and each result is a result type with an optional amount and summary, saved when the team acts. The request's date
        key is set from its date. Rock 20 and 21 save the same columns. No government ID is recorded, and nothing is given
        from a fund: the amounts are results on the request, as Rock's own Benevolence screens show them.

        Reason: Decisions 350 and 352. The Benevolence list, each request, and the person's
        Benevolence tab read the way Rock's block left them.
    */

    /// <summary>
    /// Writes the benevolence types and their security, the Cast's requests, and generated requests, with their results
    /// and signed assistance agreements.
    /// </summary>
    internal sealed class BenevolenceLoader
    {
        /// <summary>
        /// Rock's Benevolence Request Documents binary file type, which requires view security.
        /// </summary>
        private static readonly Guid DocumentFileTypeGuid = new Guid( "2599DDBE-40E1-158E-4436-AACA6C00556E" );

        private static readonly Guid RequestStatusTypeGuid = new Guid( "2787B088-D607-4D69-84FF-850A6891EE34" );
        private static readonly Guid ResultTypeTypeGuid = new Guid( "35FC0225-3DAC-48B4-BDF7-AFDE104FB60E" );
        private static readonly Guid PhoneTypeTypeGuid = new Guid( "8345DD45-73C6-4F5E-BEBD-B77FC83F18FD" );

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public BenevolenceLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// Writes everything. Returns a line for the load log.
        /// </summary>
        public string Load( PeopleLoader people, ISet<string> castHouseholdKeys, DateTime historyStart )
        {
            var config = _source.Benevolence;
            var typeIds = WriteTypes( config );
            var triggers = WriteWorkflow( config, typeIds, people );
            var statusIds = DefinedValues( RequestStatusTypeGuid );
            var resultTypeIds = DefinedValues( ResultTypeTypeGuid );
            var byGuid = people.People;
            var caseWorker = byGuid[config.CaseWorker.Person];

            var requests = config.Cast.Where( c => byGuid.ContainsKey( c.Requester ) ).Select( c => CastRequest( c, byGuid ) ).ToList();
            var generated = GeneratedRequests( config, people, castHouseholdKeys, historyStart, caseWorker );
            requests.AddRange( generated );

            var requesters = ReadRequesters( requests.Select( r => r.Requester ).Distinct().ToList() );
            var organization = OrganizationName();
            var fileType = BinaryFileRows.Type( _db, DocumentFileTypeGuid );
            var files = new BinaryFileRows( _db, _foreignKey );
            var rows = TableFactory.Create( "Id:int", "FirstName:string", "LastName:string", "Email:string", "RequestedByPersonAliasId:int", "RequestText:string", "RequestDateTime:date", "RequestDateKey:int",
                "HomePhoneNumber:string", "CellPhoneNumber:string", "WorkPhoneNumber:string", "CaseWorkerPersonAliasId:int", "RequestStatusValueId:int", "ResultSummary:string", "ConnectionStatusValueId:int",
                "LocationId:int", "ProvidedNextSteps:string", "CampusId:int", "BenevolenceTypeId:int", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "Guid:guid", "ForeignKey:string",
                "CreatedDateTime:date", "ModifiedDateTime:date" );
            var results = TableFactory.Create( "BenevolenceRequestId:int", "ResultTypeValueId:int", "Amount:decimal", "ResultSummary:string", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var documents = TableFactory.Create( "BenevolenceRequestId:int", "BinaryFileId:int", "Order:int", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "Guid:guid", "ForeignKey:string",
                "CreatedDateTime:date", "ModifiedDateTime:date" );

            var nextId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [BenevolenceRequest]" ) );
            foreach ( var request in requests.OrderBy( r => r.Requested ).ThenBy( r => r.Guid ) )
            {
                var id = nextId++;
                var who = requesters[request.Requester.PersonId];
                var lastChange = request.Results.Select( r => r.Paid ).Append( request.Decided ?? request.Requested ).Max();
                rows.Rows.Add( id, who.FirstName, who.LastName, ( object ) who.Email ?? DBNull.Value, request.Requester.AliasId, request.Text, request.Requested, int.Parse( request.Requested.ToString( "yyyyMMdd" ) ),
                    who.HomePhone, who.CellPhone, who.WorkPhone, caseWorker.AliasId, statusIds[request.Status], ( object ) request.ResultSummary ?? DBNull.Value, ( object ) who.ConnectionStatusValueId ?? DBNull.Value,
                    ( object ) who.LocationId ?? DBNull.Value, ( object ) request.NextSteps ?? DBNull.Value, ( object ) who.CampusId ?? DBNull.Value, typeIds[request.Type],
                    request.EnteredBy.AliasId, ( request.DecidedBy ?? request.EnteredBy ).AliasId, request.Guid, _foreignKey, request.Requested, lastChange );

                var order = 0;
                foreach ( var result in request.Results )
                {
                    results.Rows.Add( id, resultTypeIds[result.Type], ( object ) result.Amount ?? DBNull.Value, result.Summary, caseWorker.AliasId, caseWorker.AliasId,
                        SeededRandom.StableGuid( $"{request.Guid}:result:{order++}" ), _foreignKey, result.Paid, result.Paid );
                }

                if ( request.HasAgreement )
                {
                    var signed = request.Decided.Value;
                    var typeName = config.Types.Single( t => t.Key == request.Type ).Name;
                    var name = $"{who.NickName ?? who.FirstName} {who.LastName}";
                    var pdf = BenevolenceAgreementPdf.Draw( config.Agreement.Title, organization, config.Agreement.Paragraphs, name, typeName, request.Requested,
                        request.Results.Select( r => (r.Summary, r.Amount) ).ToList(), $"{caseWorker.Cast.NickName ?? caseWorker.Cast.FirstName} {caseWorker.Cast.LastName}", signed.Date );
                    var fileId = files.AddPdf( fileType, BinaryFileRows.PdfFileName( $"{config.Agreement.Title} {name}" ), pdf, SeededRandom.StableGuid( $"{request.Guid}:agreement" ),
                        SeededRandom.StableGuid( $"{request.Guid}:agreement-data" ), signed );
                    documents.Rows.Add( id, fileId, 0, caseWorker.AliasId, caseWorker.AliasId, SeededRandom.StableGuid( $"{request.Guid}:agreement-document" ), _foreignKey, signed, signed );
                }
            }

            files.Write( _db );
            _db.BulkInsert( "BenevolenceRequest", rows, keepIdentity: true );
            _db.BulkInsert( "BenevolenceResult", results, keepIdentity: false );
            _db.BulkInsert( "BenevolenceRequestDocument", documents, keepIdentity: false );

            return $"  Benevolence: {requests.Count:N0} requests ({requests.Count - generated.Count:N0} Cast; {requests.Count( r => r.Status == "Approved" ):N0} approved, {requests.Count( r => r.Status == "Denied" ):N0} denied, "
                + $"{requests.Count( r => r.Status == "Pending" ):N0} pending), {results.Rows.Count:N0} results, {documents.Rows.Count:N0} agreements, {typeIds.Count:N0} types, {triggers:N0} request started triggers";
        }

        /*
            11/19/26 - CLAUDE

            Rock's Benevolence Type Detail block saves a workflow trigger with the type, the workflow type, the trigger
            (Request Started), and its qualifier, "|" around two empty status Ids. When a request is added, Rock's
            BenevolenceRequestChangeTransaction activates the workflow type and processes it with the request as its entity.
            This one sets the request's case worker to the Care Director when it has none, through Rock's Entity Property Set
            action, and sends no email. It is not persisted, so a run that finishes at once saves no workflow, only the
            request's case worker, which every loaded request already has.

            Reason: Decision 355. The types show the church's trigger, and the requests agree with it.
        */

        /// <summary>
        /// Writes the Request Started workflow type and its trigger on every church type. Returns how many triggers.
        /// </summary>
        private int WriteWorkflow( BenevolenceConfiguration config, Dictionary<string, int> typeIds, PeopleLoader people )
        {
            var workflow = config.Workflow;
            if ( workflow == null )
            {
                return 0;
            }

            Guid G( string part ) => SeededRandom.StableGuid( $"benevolence-workflow:{part}" );
            var writer = new WorkflowTypeWriter( _db, _foreignKey );
            var author = people.People.TryGetValue( workflow.Author, out var person ) ? person
                : throw new InvalidDataException( $"configuration/benevolence.json: the workflow's author {workflow.Author} is not a loaded person." );
            var caseWorker = people.People[config.CaseWorker.Person];
            var built = _dates.Anchor.AddYears( -workflow.BuiltYearsAgo ).AddHours( 14 ).AddMinutes( 20 );
            var categoryId = _db.RequireIdByGuid( "Category", _source.Categories.Get( workflow.Category ).Guid );
            var typeId = writer.InsertType( workflow.Name, workflow.Description, categoryId, workflow.WorkTerm, workflow.IconCssClass, string.Empty, workflow.Guid, built, author.AliasId, isPersisted: false );
            // Rock's Entity Property Set action reads only the workflow's merge fields, not the request, so the first two
            // actions copy the request's Guid and its case worker into workflow attributes, as Rock's own examples do.
            writer.InsertWorkflowAttribute( typeId, "Rock.Field.Types.TextFieldType", "Request", "Request", "The benevolence request's Guid.", 0, false, null, G( "attribute:Request" ), built );
            writer.InsertWorkflowAttribute( typeId, "Rock.Field.Types.TextFieldType", "CaseWorker", "Case Worker", "The request's case worker, by person alias Id, when it has one.", 1, false, null,
                G( "attribute:CaseWorker" ), built );
            var activityId = writer.InsertActivityType( typeId, "Start", G( "activity" ), built, author.AliasId );
            var requestEntityType = ( Guid ) _db.Scalar( "SELECT [Guid] FROM [EntityType] WHERE [Name] = 'Rock.Model.BenevolenceRequest'" );
            var setFromEntity = writer.EntityType( "Rock.Workflow.Action.SetAttributeFromEntity" );
            var assign = $"{{% assign caseWorker = Workflow | Attribute:'CaseWorker' %}}{{% if caseWorker == empty %}}{{{{ '{caseWorker.Cast.Guid}' | PersonByGuid | Property:'PrimaryAliasId' }}}}{{% endif %}}";
            var actions = new (string Name, int EntityTypeId, (string Key, string Value)[] Settings)[]
            {
                ("Set Request", setFromEntity, new[] { ("Attribute", G( "attribute:Request" ).ToString()), ("LavaTemplate", "{{ Entity.Guid }}") }),
                ("Set Case Worker", setFromEntity, new[] { ("Attribute", G( "attribute:CaseWorker" ).ToString()), ("LavaTemplate", "{{ Entity.CaseWorkerPersonAliasId }}") }),
                ($"Assign {config.CaseWorker.Name}", writer.EntityType( "Rock.Workflow.Action.SetEntityProperty" ), new[]
                {
                    ("EntityType", requestEntityType.ToString()), ("EntityIdGuid", "{{ Workflow | Attribute:'Request' }}"), ("PropertyName", "CaseWorkerPersonAliasId"),
                    ("PropertyValue", assign), ("EmptyValueHandling", "IGNORE")
                })
            };
            for ( var i = 0; i < actions.Length; i++ )
            {
                var index = i;
                writer.InsertActionType( activityId, actions[i].Name, i, actions[i].EntityTypeId, i == actions.Length - 1, null, G( $"action:{i}" ), built, author.AliasId,
                    actions[i].Settings, key => G( $"action:{index}:{key}" ) );
            }

            var triggers = 0;
            foreach ( var type in config.Types.Where( t => t.Name != null ) )
            {
                _db.Execute( @"
INSERT INTO [BenevolenceWorkflow] ( [BenevolenceTypeId], [WorkflowTypeId], [TriggerType], [QualifierValue], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [Guid], [ForeignKey] )
VALUES ( @TypeId, @WorkflowTypeId, 0, '|||', @Built, @Built, @Author, @Author, @Guid, @ForeignKey )",
                    P( "@TypeId", typeIds[type.Key] ), P( "@WorkflowTypeId", typeId ), P( "@Built", built ), P( "@Author", author.AliasId ), P( "@Guid", G( $"trigger:{type.Key}" ) ), P( "@ForeignKey", _foreignKey ) );
                triggers++;
            }

            return triggers;
        }

        /// <summary>
        /// Adds the church's types beside Rock's default one, and locks every type to the roles Rock's benevolence pages
        /// allow. Returns each type's Id by key.
        /// </summary>
        private Dictionary<string, int> WriteTypes( BenevolenceConfiguration config )
        {
            var now = _dates.Anchor.AddYears( -3 );
            var security = new SecurityRuleWriter( _db, _source.SecurityRoles, _foreignKey );
            var ids = new Dictionary<string, int>();
            foreach ( var type in config.Types )
            {
                var id = _db.IdByGuid( "BenevolenceType", type.Guid );
                if ( id == null )
                {
                    id = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [BenevolenceType] ( [Name], [Description], [IsActive], [ShowFinancialResults], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Description, 1, 1, @Guid, @ForeignKey, @Now, @Now )",
                        P( "@Name", type.Name ), P( "@Description", type.Description ), P( "@Guid", type.Guid ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) ) );
                }

                security.Write( "Rock.Model.BenevolenceType", id.Value, config.Security, now, $"configuration/benevolence.json type {type.Key}" );
                ids[type.Key] = id.Value;
            }

            return ids;
        }

        private PlannedRequest CastRequest( CastBenevolenceRequest cast, Dictionary<Guid, LoadedPerson> people )
        {
            var requested = _dates.Resolve( cast.Requested ).Value;
            return new PlannedRequest
            {
                Guid = cast.Guid,
                Requester = people[cast.Requester],
                Type = cast.Type,
                Requested = requested,
                EnteredBy = people[cast.EnteredBy],
                Decided = _dates.Resolve( cast.Decided ),
                DecidedBy = people.GetValueOrDefault( cast.DecidedBy ),
                Status = cast.Status,
                Text = cast.Text,
                ResultSummary = cast.ResultSummary,
                NextSteps = cast.NextSteps,
                Results = cast.Results.Select( r => new PlannedResult { Type = r.Type, Amount = r.Amount, Summary = r.Summary, Paid = _dates.Resolve( r.Paid ).Value } ).ToList(),
                HasAgreement = cast.Agreement
            };
        }

        /*
            11/19/26 - CLAUDE

            Calibration has no measure of benevolence, so the rate is designed: about 2 requests a year for every 1,000
            adults, over the history window. People the church knows, but who are not yet deeply involved, ask most, and a
            family asks once. Each request's day, type, decision, and help come from its own stream, seeded by the
            requester, so adding a person or a rule elsewhere moves nothing here.

            Reason: Decision 352. Full 7 gets a few dozen requests, and Core 1, which is only the Cast, gets the
            Cast's stories.
        */

        private List<PlannedRequest> GeneratedRequests( BenevolenceConfiguration config, PeopleLoader people, ISet<string> castHouseholdKeys, DateTime historyStart, LoadedPerson caseWorker )
        {
            var rule = config.Generated;
            var adults = people.Households
                .Where( h => !castHouseholdKeys.Contains( h.Cast.Key ) && h.Cast.Kind != "business" )
                .SelectMany( h => h.Members.Where( m => m.IsAdult && m.CanAttend && m.BirthDate.HasValue && _dates.AgeOn( m.BirthDate.Value ) >= 18 ).Select( m => (Household: h, Person: m) ) )
                .ToList();
            var years = ( _dates.Anchor - historyStart ).TotalDays / 365.25;
            var count = ( int ) Math.Round( adults.Count * rule.PerThousandAdultsPerYear / 1000 * years );

            // Weighted draw without replacement (each key is -ln(u) / weight; the smallest win), one request per family.
            var picks = new SeededRandom( $"{_seed}:benevolence:requesters" );
            var chosen = adults
                .Select( a => (a.Household, a.Person, Weight: rule.RequesterWeights.GetValueOrDefault( a.Person.Cast.ConnectionStatus ?? string.Empty )) )
                .Where( a => a.Weight > 0 )
                .Select( a => (a.Household, a.Person, Key: -Math.Log( 1 - picks.Between( 0.0, 1.0 ) ) / a.Weight) )
                .OrderBy( a => a.Key )
                .GroupBy( a => a.Household.FamilyId )
                .Select( g => g.First() )
                .OrderBy( a => a.Key )
                .Take( count )
                .ToList();

            var requests = new List<PlannedRequest>();
            var firstDay = historyStart.Date;
            var lastDay = _dates.Anchor.AddDays( -1 );
            foreach ( var (_, person, _) in chosen )
            {
                var random = new SeededRandom( $"{_seed}:benevolence:request:{person.Cast.Guid}" );
                var requested = Weekday( firstDay.AddDays( random.Between( 0, ( lastDay - firstDay ).Days ) ) ).Add( TimeSpan.FromMinutes( random.Between( 9 * 60, 16 * 60 ) ) );
                if ( requested.Date > lastDay )
                {
                    requested = lastDay.AddHours( 10 );
                }

                var type = random.Pick( rule.Types );
                var decided = Weekday( requested.Date.AddDays( random.Between( rule.DecidedDaysAfter[0], rule.DecidedDaysAfter[1] ) ) ).Add( TimeSpan.FromMinutes( random.Between( 9 * 60, 17 * 60 ) ) );
                var isPending = requested >= _dates.Anchor.AddDays( -rule.PendingDays ) || decided >= _dates.Anchor;
                var status = isPending ? "Pending" : random.Pick( rule.Status );
                var request = new PlannedRequest
                {
                    Guid = SeededRandom.StableGuid( $"{person.Cast.Guid}:benevolence-request" ),
                    Requester = person,
                    Type = type,
                    Requested = requested,
                    EnteredBy = caseWorker,
                    Status = status,
                    Text = random.Pick( config.Text.Requests[type] )
                };

                if ( status == "Approved" )
                {
                    var help = Pick( random, config.Results[type] );
                    var paid = decided.Date.AddDays( random.Between( rule.PaidDaysAfterDecision[0], rule.PaidDaysAfterDecision[1] ) ).AddHours( 11 ).AddMinutes( random.Between( 0, 240 ) );
                    request.Decided = decided;
                    request.DecidedBy = caseWorker;
                    request.ResultSummary = help.Summary;
                    request.NextSteps = random.Pick( config.Text.NextSteps );
                    request.Results.Add( new PlannedResult { Type = help.Type, Amount = Amount( random, help ), Summary = help.Summary, Paid = paid < _dates.Anchor ? paid : decided } );
                    request.HasAgreement = rule.AgreementTypes.Contains( type ) && request.Results.Any( r => r.Amount.HasValue );
                }
                else if ( status == "Denied" )
                {
                    request.Decided = decided;
                    request.DecidedBy = caseWorker;
                    request.ResultSummary = random.Pick( config.Text.Denied );
                    request.NextSteps = random.Chance( 0.5 ) ? random.Pick( config.Text.NextSteps ) : null;
                }

                requests.Add( request );
            }

            return requests;
        }

        private static BenevolenceResultRule Pick( SeededRandom random, List<BenevolenceResultRule> rules )
        {
            var roll = random.Between( 0.0, rules.Sum( r => r.Weight ) );
            foreach ( var rule in rules )
            {
                roll -= rule.Weight;
                if ( roll < 0 )
                {
                    return rule;
                }
            }

            return rules[rules.Count - 1];
        }

        private static decimal? Amount( SeededRandom random, BenevolenceResultRule rule )
        {
            if ( rule.Amount == null )
            {
                return null;
            }

            var cents = random.Between( ( int ) ( rule.Amount[0] * 100 ), ( int ) ( rule.Amount[1] * 100 ) );
            var amount = cents / 100m;
            return rule.Round.HasValue ? Math.Max( rule.Round.Value, Math.Round( amount / rule.Round.Value ) * rule.Round.Value ) : amount;
        }

        /// <summary>
        /// The day itself on a weekday, or the Monday after a weekend, when the church office takes requests.
        /// </summary>
        private static DateTime Weekday( DateTime day ) => day.DayOfWeek switch
        {
            DayOfWeek.Saturday => day.AddDays( 2 ),
            DayOfWeek.Sunday => day.AddDays( 1 ),
            _ => day
        };

        /// <summary>
        /// What Rock's block copies from each requester onto the request.
        /// </summary>
        private Dictionary<int, Requester> ReadRequesters( List<LoadedPerson> people )
        {
            var ids = string.Join( ",", people.Select( p => p.PersonId ) );
            if ( ids.Length == 0 )
            {
                return new Dictionary<int, Requester>();
            }

            var phones = _db.Query( $@"
SELECT [pn].[PersonId], [dv].[Value] AS [Type], [pn].[NumberFormatted]
FROM [PhoneNumber] AS [pn]
INNER JOIN [DefinedValue] AS [dv] ON [dv].[Id] = [pn].[NumberTypeValueId]
INNER JOIN [DefinedType] AS [dt] ON [dt].[Id] = [dv].[DefinedTypeId]
WHERE [dt].[Guid] = @PhoneTypes AND [pn].[PersonId] IN ( {ids} )", P( "@PhoneTypes", PhoneTypeTypeGuid ) )
                .GroupBy( r => Convert.ToInt32( r["PersonId"] ) )
                .ToDictionary( g => g.Key, g => g.GroupBy( r => ( string ) r["Type"] ).ToDictionary( t => t.Key, t => ( string ) t.First()["NumberFormatted"] ) );

            return _db.Query( $@"
SELECT [p].[Id], [p].[FirstName], [p].[NickName], [p].[LastName], NULLIF( [p].[Email], '' ) AS [Email], [p].[ConnectionStatusValueId], [p].[PrimaryCampusId],
    ( SELECT TOP 1 [gl].[LocationId] FROM [GroupLocation] AS [gl] INNER JOIN [DefinedValue] AS [t] ON [t].[Id] = [gl].[GroupLocationTypeValueId]
      WHERE [gl].[GroupId] = [p].[PrimaryFamilyId] AND [t].[Guid] = '8C52E53C-2A66-435A-AE6E-5EE307D9A0DC' ORDER BY [gl].[Id] ) AS [LocationId]
FROM [Person] AS [p]
WHERE [p].[Id] IN ( {ids} )" )
                .ToDictionary( r => Convert.ToInt32( r["Id"] ), r =>
                {
                    var own = phones.GetValueOrDefault( Convert.ToInt32( r["Id"] ) ) ?? new Dictionary<string, string>();
                    return new Requester
                    {
                        FirstName = ( string ) r["FirstName"],
                        NickName = r["NickName"] as string,
                        LastName = ( string ) r["LastName"],
                        Email = r["Email"] as string,
                        ConnectionStatusValueId = r["ConnectionStatusValueId"] as int?,
                        CampusId = r["PrimaryCampusId"] as int?,
                        LocationId = r["LocationId"] as int?,
                        HomePhone = own.GetValueOrDefault( "Home" ) ?? string.Empty,
                        CellPhone = own.GetValueOrDefault( "Mobile" ) ?? string.Empty,
                        WorkPhone = own.GetValueOrDefault( "Work" ) ?? string.Empty
                    };
                } );
        }

        private Dictionary<string, int> DefinedValues( Guid typeGuid )
        {
            return _db.Query( @"
SELECT [dv].[Value], [dv].[Id]
FROM [DefinedValue] AS [dv]
INNER JOIN [DefinedType] AS [dt] ON [dt].[Id] = [dv].[DefinedTypeId]
WHERE [dt].[Guid] = @Guid", P( "@Guid", typeGuid ) ).ToDictionary( r => ( string ) r["Value"], r => Convert.ToInt32( r["Id"] ) );
        }

        private string OrganizationName()
        {
            return _db.Scalar( "SELECT TOP 1 ISNULL( NULLIF( [av].[Value], '' ), [a].[DefaultValue] ) FROM [Attribute] AS [a] LEFT JOIN [AttributeValue] AS [av] ON [av].[AttributeId] = [a].[Id] WHERE [a].[Key] = 'OrganizationName' AND [a].[EntityTypeId] IS NULL" ) as string
                ?? throw new InvalidOperationException( "Rock's OrganizationName global attribute was not found." );
        }

        private sealed class PlannedRequest
        {
            public Guid Guid { get; set; }

            public LoadedPerson Requester { get; set; }

            public string Type { get; set; }

            public DateTime Requested { get; set; }

            public LoadedPerson EnteredBy { get; set; }

            public DateTime? Decided { get; set; }

            public LoadedPerson DecidedBy { get; set; }

            public string Status { get; set; }

            public string Text { get; set; }

            public string ResultSummary { get; set; }

            public string NextSteps { get; set; }

            public List<PlannedResult> Results { get; set; } = new List<PlannedResult>();

            public bool HasAgreement { get; set; }
        }

        private sealed class PlannedResult
        {
            public string Type { get; set; }

            public decimal? Amount { get; set; }

            public string Summary { get; set; }

            public DateTime Paid { get; set; }
        }

        private sealed class Requester
        {
            public string FirstName { get; set; }

            public string NickName { get; set; }

            public string LastName { get; set; }

            public string Email { get; set; }

            public int? ConnectionStatusValueId { get; set; }

            public int? CampusId { get; set; }

            public int? LocationId { get; set; }

            public string HomePhone { get; set; }

            public string CellPhone { get; set; }

            public string WorkPhone { get; set; }
        }
    }
}
