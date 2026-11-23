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
using RockSampleData.Loader.Loading;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Validation
{
    /// <summary>
    /// Checks the Cast's Data Integrity stories in configuration/data-integrity.json against the loaded database: each
    /// record's date, the Pending records' forms, the self-inactivations, the connection status changes, the Person Data
    /// Error and Photo Request workflows, the couples who share an email, the failed addresses, and the Data Automation
    /// rules.
    /// </summary>
    internal static class CastDataIntegrityExpectations
    {
        private static readonly Guid PendingStatusGuid = new Guid( "283999EC-7346-42E3-B807-BCE9B2BABB49" );
        private static readonly Guid InactiveStatusGuid = new Guid( "1DAD99D5-41A9-4865-8366-F269902B80A4" );
        private static readonly Guid SelfInactivatedGuid = new Guid( "D539C356-6856-4E94-80B4-8FEA869AF38B" );

        /// <summary>
        /// Returns how many expectations were checked, and adds a line for each one that does not hold.
        /// </summary>
        public static int Check( RockDatabase db, SampleDataSource source, CastPersonGuids guids, DateTime anchor, bool arePhotosLoaded, List<string> failures )
        {
            var config = source.DataIntegrity;
            if ( config == null )
            {
                return 0;
            }

            var dates = new AnchorDates( anchor );
            var households = source.ReadHouseholds();
            var people = db.Query( @"
SELECT [p].[Id], [p].[Guid], [p].[CreatedDateTime], [p].[ConnectionStatusValueId], [p].[RecordStatusValueId], [p].[PrimaryAliasId], [p].[NickName] + ' ' + [p].[LastName] AS [Name]
FROM [Person] AS [p]
WHERE [p].[ForeignKey] LIKE 'SampleData:%' OR [p].[Id] = @Admin", P( "@Admin", PeopleLoader.AdminPersonId ) )
                .ToDictionary( r => ( Guid ) r["Guid"] );
            Dictionary<string, object> Find( Guid castGuid ) => people.TryGetValue( guids.Rock( castGuid ), out var row ) ? row : null;

            var checks = RecordDates( db, households, people, anchor, failures );
            checks += Pending( db, config.Pending, dates, Find, failures );
            checks += SelfInactivations( db, config.SelfInactivations, households, dates, Find, failures );
            checks += StatusHistory( db, source, config.ConnectionStatusHistory, dates, guids, Find, failures );
            checks += DataErrors( db, config.DataErrors, Find, failures );
            checks += PhotoRequests( db, config.PhotoRequests, arePhotosLoaded, Find, failures );
            checks += SharedEmails( db, config.SharedEmails, households, failures );
            checks += FailedAddresses( db, config.FailedAddresses, households, Find, failures );
            checks += Settings( db, config.Settings, failures );
            return checks;
        }

        /// <summary>
        /// Every Cast person but Rock's admin has a record date before the install day, and no record of theirs is older.
        /// </summary>
        private static int RecordDates( RockDatabase db, List<CastHousehold> households, Dictionary<Guid, Dictionary<string, object>> people, DateTime anchor, List<string> failures )
        {
            var cast = households.SelectMany( h => h.Members ).Where( m => !m.ReplacesAdmin && people.ContainsKey( m.Guid ) ).Select( m => people[m.Guid] ).ToList();
            var firstRecords = DataIntegrityLoader.FirstRecords( db, cast.Select( p => ( int ) p["Id"] ) );
            foreach ( var person in cast )
            {
                var created = ( DateTime ) person["CreatedDateTime"];
                if ( created > anchor.Date )
                {
                    failures.Add( $"{person["Name"]}: expected a record date before the install day, Rock shows {created:yyyy-MM-dd HH:mm}" );
                }
                else if ( firstRecords.TryGetValue( ( int ) person["Id"], out var first ) && first < created )
                {
                    failures.Add( $"{person["Name"]}: the record is dated {created:yyyy-MM-dd HH:mm}, after their first record at {first:yyyy-MM-dd HH:mm}" );
                }
            }

            return cast.Count;
        }

        private static int Pending( RockDatabase db, PendingRecords pending, AnchorDates dates, Func<Guid, Dictionary<string, object>> find, List<string> failures )
        {
            var pendingId = db.RequireIdByGuid( "DefinedValue", PendingStatusGuid );
            var checks = 0;
            foreach ( var record in pending.Records )
            {
                var person = find( record.Person );
                if ( person == null )
                {
                    continue;
                }

                checks++;
                var source = db.Scalar( "SELECT [v].[Value] FROM [Person] AS [p] INNER JOIN [DefinedValue] AS [v] ON [v].[Id] = [p].[RecordSourceValueId] WHERE [p].[Id] = @Id", P( "@Id", person["Id"] ) ) as string;
                if ( ( int ) person["RecordStatusValueId"] != pendingId )
                {
                    failures.Add( $"{record.Name}: expected a Pending record" );
                }
                else if ( source != record.Source )
                {
                    failures.Add( $"{record.Name}: expected the Record Source {record.Source ?? "none"}, Rock shows {source ?? "none"}" );
                }
                else if ( record.Created != null && ( DateTime ) person["CreatedDateTime"] > dates.Resolve( record.Created ).Value )
                {
                    failures.Add( $"{record.Name}: expected a record made by {dates.Resolve( record.Created ):yyyy-MM-dd HH:mm}, Rock shows {person["CreatedDateTime"]:yyyy-MM-dd HH:mm}" );
                }
            }

            return checks;
        }

        private static int SelfInactivations( RockDatabase db, SelfInactivations inactivations, List<CastHousehold> households, AnchorDates dates, Func<Guid, Dictionary<string, object>> find,
            List<string> failures )
        {
            var inactiveId = db.RequireIdByGuid( "DefinedValue", InactiveStatusGuid );
            var reviewId = db.RequireIdByGuid( "DefinedValue", SelfInactivatedGuid );
            var checks = 0;
            foreach ( var record in inactivations.Records.Where( r => find( r.Person ) != null ) )
            {
                // The whole household, for an adult in one family, as Rock's Email Preferences page inactivates it.
                var household = households.First( h => h.Members.Any( m => m.Guid == record.Person ) );
                var person = household.Members.First( m => m.Guid == record.Person );
                var targets = person.FamilyRole != "child" && !households.Any( h => h.SharedMembers.Any( s => s.Guid == record.Person ) ) ? household.Members : new List<CastPerson> { person };
                foreach ( var target in targets.Where( t => find( t.Guid ) != null ) )
                {
                    checks++;
                    var row = db.Query( @"
SELECT [p].[RecordStatusValueId], [r].[Value] AS [Reason], [p].[InactiveReasonNote], [p].[ReviewReasonValueId], [p].[ReviewReasonNote], [p].[RecordStatusLastModifiedDateTime],
    ( SELECT COUNT(*) FROM [History] AS [h] WHERE [h].[EntityId] = [p].[Id] AND [h].[ValueName] = 'Review Reason' AND [h].[CreatedDateTime] = [p].[RecordStatusLastModifiedDateTime] ) AS [Lines]
FROM [Person] AS [p] LEFT JOIN [DefinedValue] AS [r] ON [r].[Id] = [p].[RecordStatusReasonValueId]
WHERE [p].[Id] = @Id", P( "@Id", find( target.Guid )["Id"] ) ).Single();
                    var name = $"{target.NickName ?? target.FirstName} {target.LastName}";
                    if ( ( int ) row["RecordStatusValueId"] != inactiveId || row["ReviewReasonValueId"] as int? != reviewId || ( string ) row["Reason"] != record.Reason )
                    {
                        failures.Add( $"{name}: expected an inactive record, {record.Reason}, marked Self Inactivated by {record.Name}" );
                    }
                    else if ( row["InactiveReasonNote"] as string != record.Note || row["ReviewReasonNote"] as string != record.Note )
                    {
                        failures.Add( $"{name}: expected {record.Name}'s note as the inactive and review reason notes" );
                    }
                    else if ( row["RecordStatusLastModifiedDateTime"] as DateTime? != dates.Resolve( record.Inactivated ) || Convert.ToInt32( row["Lines"] ) != 1 )
                    {
                        failures.Add( $"{name}: expected the status change and its History lines at {dates.Resolve( record.Inactivated ):yyyy-MM-dd HH:mm}" );
                    }
                }
            }

            return checks;
        }

        private static int StatusHistory( RockDatabase db, SampleDataSource source, ConnectionStatusHistory history, AnchorDates dates, CastPersonGuids guids,
            Func<Guid, Dictionary<string, object>> find, List<string> failures )
        {
            var statusIds = source.ConnectionStatuses.Statuses.ToDictionary( s => s.Name, s => db.RequireIdByGuid( "DefinedValue", s.Guid ), StringComparer.OrdinalIgnoreCase );
            var checks = 0;
            foreach ( var entry in history.People )
            {
                foreach ( var named in entry.People.Where( p => find( p.Person ) != null ) )
                {
                    var person = find( named.Person );
                    checks++;
                    if ( ( int ) person["ConnectionStatusValueId"] != statusIds[entry.Changes.Last().To] )
                    {
                        failures.Add( $"{named.Name}: expected the connection status {entry.Changes.Last().To}, the last change in their history" );
                    }

                    foreach ( var change in entry.Changes )
                    {
                        checks++;
                        var found = Convert.ToInt32( db.Scalar( @"
SELECT COUNT(*)
FROM [History] AS [h]
INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [h].[CreatedByPersonAliasId]
INNER JOIN [Person] AS [by] ON [by].[Id] = [pa].[PersonId]
WHERE [h].[EntityId] = @Id AND [h].[ValueName] = @ValueName AND [h].[OldValue] = @From AND [h].[NewValue] = @To AND [h].[OldRawValue] = @FromId AND [h].[NewRawValue] = @ToId
    AND [h].[CreatedDateTime] = @When AND [by].[Guid] = @By",
                            P( "@Id", person["Id"] ), P( "@ValueName", DataIntegrityLoader.ConnectionStatusValueName ), P( "@From", change.From ), P( "@To", change.To ),
                            P( "@FromId", statusIds[change.From].ToString() ), P( "@ToId", statusIds[change.To].ToString() ), P( "@When", dates.Resolve( change.Changed ).Value ), P( "@By", guids.Value( change.By ) ) ) );
                        if ( found != 1 )
                        {
                            failures.Add( $"{named.Name}: expected one History line for the change from {change.From} to {change.To} on {dates.Resolve( change.Changed ):yyyy-MM-dd}, Rock shows {found}" );
                        }
                    }
                }
            }

            return checks;
        }

        private static int DataErrors( RockDatabase db, DataErrorReports reports, Func<Guid, Dictionary<string, object>> find, List<string> failures )
        {
            var checks = 0;
            foreach ( var report in reports.Reports.Where( r => find( r.Person ) != null && find( r.ReportedBy ) != null ) )
            {
                checks++;
                var workflow = db.Query( @"
SELECT [w].[Status], [w].[Name],
    ( SELECT TOP 1 [av].[Value] FROM [AttributeValue] AS [av] INNER JOIN [Attribute] AS [a] ON [a].[Id] = [av].[AttributeId] WHERE [av].[EntityId] = [w].[Id] AND [a].[Key] = 'Resolution'
        AND [a].[EntityTypeQualifierColumn] = 'WorkflowTypeId' AND [a].[EntityTypeQualifierValue] = CAST( [w].[WorkflowTypeId] AS NVARCHAR( 50 ) ) ) AS [Resolution],
    ( SELECT TOP 1 [pa].[PersonId] FROM [WorkflowActivity] AS [act] INNER JOIN [PersonAlias] AS [pa] ON [pa].[Id] = [act].[AssignedPersonAliasId] WHERE [act].[WorkflowId] = [w].[Id] AND [act].[CompletedDateTime] IS NULL ) AS [WaitsOn]
FROM [Workflow] AS [w]
INNER JOIN [WorkflowType] AS [t] ON [t].[Id] = [w].[WorkflowTypeId] AND [t].[Guid] = @Type
INNER JOIN [PersonAlias] AS [init] ON [init].[Id] = [w].[InitiatorPersonAliasId]
WHERE [w].[EntityId] = @Person AND [init].[PersonId] = @Reporter",
                    P( "@Type", DataIntegrityLoader.PersonDataErrorTypeGuid ), P( "@Person", find( report.Person )["Id"] ), P( "@Reporter", find( report.ReportedBy )["Id"] ) ).FirstOrDefault();
                if ( workflow == null )
                {
                    failures.Add( $"{report.Name}: expected the {report.Key} Person Data Error report, Rock has none" );
                }
                else if ( report.IsResolved && ( ( string ) workflow["Status"] != "Completed" || workflow["Resolution"] as string != report.Resolution ) )
                {
                    failures.Add( $"{report.Name}: expected the {report.Key} report completed with its resolution" );
                }
                else if ( !report.IsResolved && ( ( string ) workflow["Status"] != "Active" || workflow["WaitsOn"] as int? != PeopleLoader.AdminPersonId ) )
                {
                    failures.Add( $"{report.Name}: expected the {report.Key} report waiting on Rock's admin person, Alisha Marble" );
                }
            }

            return checks;
        }

        private static int PhotoRequests( RockDatabase db, PhotoRequests requests, bool arePhotosLoaded, Func<Guid, Dictionary<string, object>> find, List<string> failures )
        {
            var checks = 0;
            foreach ( var request in requests.Requests.Where( r => find( r.Person ) != null ) )
            {
                checks++;
                var person = find( request.Person );
                var sent = Convert.ToInt32( db.Scalar( @"
SELECT COUNT(*) FROM [Workflow] AS [w] INNER JOIN [WorkflowType] AS [t] ON [t].[Id] = [w].[WorkflowTypeId] AND [t].[Guid] = @Type
WHERE [w].[EntityId] = @Person AND [w].[Status] = 'Completed'", P( "@Type", DataIntegrityLoader.PhotoRequestTypeGuid ), P( "@Person", person["Id"] ) ) );
                var status = db.Scalar( @"
SELECT [gm].[GroupMemberStatus] FROM [GroupMember] AS [gm] INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId] AND [g].[Guid] = @Group WHERE [gm].[PersonId] = @Person",
                    P( "@Group", DataIntegrityLoader.PhotoRequestGroupGuid ), P( "@Person", person["Id"] ) ) as int?;
                int? expected = request.OptedOut != null ? 0 : request.Uploaded == null || !arePhotosLoaded ? null : request.Verified != null ? 1 : 2;
                if ( sent != 1 )
                {
                    failures.Add( $"{request.Name}: expected one Photo Request sent, Rock has {sent}" );
                }
                else if ( status != expected )
                {
                    var names = new Dictionary<int, string> { [0] = "Opted Out", [1] = "Verified", [2] = "Pending" };
                    failures.Add( $"{request.Name}: expected {( expected.HasValue ? names[expected.Value] : "no reply" )} in the Photo Request group, Rock shows {( status.HasValue ? names[status.Value] : "no reply" )}" );
                }
            }

            return checks;
        }

        private static int SharedEmails( RockDatabase db, SharedEmails shared, List<CastHousehold> households, List<string> failures )
        {
            var checks = 0;
            foreach ( var entry in shared.Households )
            {
                var household = households.FirstOrDefault( h => h.Key == entry.Household );
                if ( household == null )
                {
                    continue;
                }

                checks++;
                var count = Convert.ToInt32( db.Scalar( "SELECT COUNT(*) FROM [Person] WHERE [Email] = @Email AND [IsDeceased] = 0", P( "@Email", entry.Email ) ) );
                if ( count != household.Members.Count( m => m.Email == entry.Email ) || count < 2 )
                {
                    failures.Add( $"{household.Name}: expected the couple to share {entry.Email}, which Rock's Individuals with Duplicate Emails report lists, Rock shows {count} people with it" );
                }
            }

            return checks;
        }

        private static int FailedAddresses( RockDatabase db, FailedAddresses failed, List<CastHousehold> households, Func<Guid, Dictionary<string, object>> find, List<string> failures )
        {
            var checks = 0;
            foreach ( var entry in failed.Households )
            {
                var household = households.FirstOrDefault( h => h.Key == entry.Household );
                var member = household?.Members.Select( m => find( m.Guid ) ).FirstOrDefault( p => p != null );
                if ( member == null )
                {
                    continue;
                }

                checks++;
                var location = db.Query( @"
SELECT [l].[IsGeoPointLocked], [l].[StandardizeAttemptedResult], [l].[GeocodeAttemptedResult], [l].[StandardizedDateTime], [l].[GeocodedDateTime], CASE WHEN [l].[GeoPoint] IS NULL THEN 0 ELSE 1 END AS [HasPoint]
FROM [Person] AS [p]
INNER JOIN [GroupLocation] AS [gl] ON [gl].[GroupId] = [p].[PrimaryFamilyId]
INNER JOIN [DefinedValue] AS [t] ON [t].[Id] = [gl].[GroupLocationTypeValueId] AND [t].[Guid] = '8C52E53C-2A66-435A-AE6E-5EE307D9A0DC'
INNER JOIN [Location] AS [l] ON [l].[Id] = [gl].[LocationId]
WHERE [p].[Id] = @Id", P( "@Id", member["Id"] ) ).FirstOrDefault();
                if ( location == null || location["IsGeoPointLocked"] as bool? != true || location["StandardizeAttemptedResult"] as string != failed.Result || location["GeocodeAttemptedResult"] as string != failed.Result
                    || location["StandardizedDateTime"] is DateTime || location["GeocodedDateTime"] is DateTime || Convert.ToInt32( location["HasPoint"] ) == 1 )
                {
                    failures.Add( $"{household.Name}: expected a locked home address with the result {failed.Result} and no map point" );
                }
            }

            return checks;
        }

        private static int Settings( RockDatabase db, DataIntegritySettings settings, List<string> failures )
        {
            foreach ( var rule in settings.Rules )
            {
                var value = db.Scalar( "SELECT [DefaultValue] FROM [Attribute] WHERE [EntityTypeId] IS NULL AND [EntityTypeQualifierColumn] = 'SystemSetting' AND [Key] = @Key", P( "@Key", rule.Key ) ) as string;
                var expected = rule.Value.GetProperty( "IsEnabled" ).GetBoolean();
                if ( value == null )
                {
                    failures.Add( $"Data Automation: expected the {rule.Key} setting, Rock has none" );
                }
                else if ( System.Text.Json.JsonDocument.Parse( value ).RootElement.GetProperty( "IsEnabled" ).GetBoolean() != expected )
                {
                    failures.Add( $"Data Automation: expected {rule.Key} {( expected ? "on" : "off" )}" );
                }
            }

            return settings.Rules.Count;
        }
    }
}
