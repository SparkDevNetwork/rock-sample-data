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

using SixLabors.ImageSharp;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Sets up how the sample church communicates (docs/communications.md): its texting numbers, its
    /// branded email template, and its communication lists with their members.
    /// </summary>
    internal sealed class CommunicationsLoader
    {
        private static readonly Guid CommunicationListGroupTypeGuid = new Guid( "D1D95777-FFA3-CBB3-4A6D-658706DAED33" );
        private static readonly Guid CommunicationListRecipientRoleGuid = new Guid( "9D85AB4E-59BC-B48A-494A-5684BA41578E" );
        private static readonly Guid GroupEntityTypeGuid = new Guid( "9BBFDA11-0D22-40D5-902F-60ADFBC88987" );
        private static readonly Guid DefaultFileTypeGuid = new Guid( "C1142570-8CD6-4A20-83B1-ACB47C1CD377" );

        /// <summary>
        /// The logo placeholder Rock's template editor replaces with the uploaded logo.
        /// </summary>
        private const string PlaceholderLogoSrc = "/Content/EmailTemplates/placeholder-logo.png";

        /// <summary>
        /// Rock's CommunicationType: a member's preference on a list.
        /// </summary>
        private const int EmailPreference = 1;
        private const int SmsPreference = 2;

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly ProfileTargets _profile;
        private readonly AnchorDates _dates;
        private readonly string _dataset;
        private readonly string _foreignKey;
        private readonly string _seed;

        public CommunicationsLoader( RockDatabase db, SampleDataSource source, ProfileTargets profile, AnchorDates dates, string dataset, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _profile = profile;
            _dates = dates;
            _dataset = dataset;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// Writes the phone numbers, the template, and the lists with their members.
        /// </summary>
        /// <param name="people">Everyone loaded.</param>
        /// <param name="castHouseholdKeys">The Cast's households. Campus pastors come from the rest.</param>
        /// <param name="mainCampusKey">The main campus.</param>
        /// <param name="campusIds">The dataset's campuses.</param>
        /// <param name="familySundays">Each family's attended Sundays, from the attendance plan.</param>
        /// <param name="gifts">Each giving unit's gifts, from the giving plan.</param>
        /// <param name="prayerTeam">The prayer team's person Ids.</param>
        /// <param name="smallGroupTypeId">The small group type.</param>
        /// <param name="servingTypeId">The serving team type.</param>
        public void Load( PeopleLoader people, HashSet<string> castHouseholdKeys, string mainCampusKey, IReadOnlyDictionary<string, int> campusIds,
            Dictionary<int, HashSet<DateTime>> familySundays, Dictionary<string, List<RockGift>> gifts, HashSet<int> prayerTeam, int smallGroupTypeId, int servingTypeId )
        {
            var configuration = _source.Communications;
            if ( configuration == null )
            {
                _db.Log.Write( "  Skipped (this package has no configuration/communications.json)" );
                return;
            }

            var numbers = AddPhoneNumbers( configuration.PhoneNumbers, people, castHouseholdKeys, mainCampusKey );
            _db.Log.Write( $"  Phone numbers: {numbers}" );

            var (templateId, templateHtml) = AddTemplate( configuration, people );
            _db.Log.Write( $"  Email template: {configuration.Template.Name}" );

            // A Cast story can put a person on a list, or keep them off every list, so their scripted unsubscribe can happen.
            var forced = configuration.CastUnsubscribes.Where( u => u.List != null ).ToLookup( u => u.List, u => u.Person );
            var excluded = configuration.CastUnsubscribes.Where( u => u.List == null ).Select( u => u.Person ).ToHashSet();
            var candidates = Candidates( people, familySundays, gifts, prayerTeam, smallGroupTypeId, servingTypeId, configuration.Lists.Membership );
            var plan = CommunicationListPlanner.Plan( configuration.Lists.Lists, configuration.Lists.Membership, candidates, campusIds.Keys.ToList(), _dates.Anchor, _seed, forced, excluded );
            var listIds = AddLists( configuration.Lists, campusIds );

            // Bulk email runs before the members are written, because an unsubscribe makes a member Inactive.
            var castPeople = people.Households.Where( h => castHouseholdKeys.Contains( h.Cast.Key ) ).SelectMany( h => h.Members ).Select( m => m.Cast.Guid );
            var emails = new BulkEmailLoader( _db, _source, _profile, _dates, _foreignKey, _seed ).WithCast( castPeople );
            emails.Load( configuration, people, candidates, plan, listIds, templateId, templateHtml, mainCampusKey, campusIds );
            WriteMembers( plan, listIds );

            var sizes = plan.GroupBy( m => (m.ListKey, m.CampusKey) ).OrderBy( g => configuration.Lists.Lists.FindIndex( l => l.Key == g.Key.ListKey ) ).ThenBy( g => g.Key.CampusKey, StringComparer.Ordinal )
                .Select( g => $"{ListName( configuration.Lists.Lists.First( l => l.Key == g.Key.ListKey ), g.Key.CampusKey )} {g.Count():N0}" );
            _db.Log.Write( $"  Communication lists: {listIds.Count} with {plan.Count:N0} members ({string.Join( ", ", sizes )})" );

            var activeAdults = candidates.Count( c => c.IsAdult );
            var onANewsletter = plan.Where( m => m.ListKey == CommunicationListPlanner.Newsletter ).Select( m => m.PersonId ).Distinct().Count();
            _db.Log.Write( $"  {onANewsletter:N0} of {activeAdults:N0} active adults ({( activeAdults == 0 ? 0 : 100.0 * onANewsletter / activeAdults ):N0}%) get a weekly newsletter" );
        }

        #region Phone numbers

        /// <summary>
        /// Adds the church's lines, the staff's personal numbers, and in some datasets each campus pastor's
        /// number. Returns a summary for the log.
        /// </summary>
        private string AddPhoneNumbers( PhoneNumberConfiguration configuration, PeopleLoader people, HashSet<string> castHouseholdKeys, string mainCampusKey )
        {
            var now = DateTime.Now;
            var names = new List<string>();
            foreach ( var number in configuration.Numbers.OrderBy( n => n.Order ) )
            {
                int? aliasId = null;
                if ( number.AssignedTo.HasValue )
                {
                    aliasId = people.People.TryGetValue( number.AssignedTo.Value, out var person )
                        ? person.AliasId
                        : throw new InvalidOperationException( $"configuration/communications.json assigns {number.Key} to {number.AssignedTo}, who is not a loaded person." );
                }

                InsertPhoneNumber( number.Name, number.Description, number.Number, number.Order, aliasId, number.Guid, now );
                names.Add( number.Name );
            }

            if ( configuration.CampusPastors.Datasets.Contains( _dataset ) )
            {
                var generated = people.Households.Where( h => !castHouseholdKeys.Contains( h.Cast.Key ) && h.Cast.Kind != "business" );
                var campusNames = _source.Campuses.Campuses.ToDictionary( c => c.Key, c => c.Name );
                var order = configuration.Numbers.Count == 0 ? 0 : configuration.Numbers.Max( n => n.Order ) + 1;
                foreach ( var (campusKey, pastor) in CampusPastors.Pick( generated, mainCampusKey ).OrderBy( p => p.Key, StringComparer.Ordinal ) )
                {
                    if ( !configuration.CampusPastors.Numbers.TryGetValue( campusKey, out var number ) )
                    {
                        throw new InvalidOperationException( $"configuration/communications.json has no campus pastor number for {campusKey}." );
                    }

                    var name = $"{pastor.Cast.NickName ?? pastor.Cast.FirstName} {pastor.Cast.LastName}";
                    InsertPhoneNumber( name, $"{name}'s personal texting number. {name} is the {campusNames[campusKey]} campus pastor.", number.Number, order++, pastor.AliasId, number.Guid, now );
                    names.Add( name );
                }
            }

            return string.Join( ", ", names );
        }

        /*
            10/23/26 - CLAUDE

            Forwarding is off on every number. Rock forwards a text that reaches a personal number to the
            staff member's own phone, and a demo database should never pass a real text along. Opt-in and
            opt-out tracking stays on, as Rock ships it, so a texted STOP works the way Rock records it.

            Reason: Nothing a load writes may ever send (decision 95).
        */

        private void InsertPhoneNumber( string name, string description, string number, int order, int? assignedToAliasId, Guid guid, DateTime now )
        {
            _db.Execute( @"
INSERT INTO [SystemPhoneNumber] ( [Name], [Description], [Number], [IsActive], [Order], [AssignedToPersonAliasId], [IsSmsEnabled], [IsSmsForwardingEnabled],
    [SuppressSmsOptInOutAutoReplies], [DisableSmsOptInOutTracking], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
VALUES ( @Name, @Description, @Number, 1, @Order, @AssignedTo, 1, 0, 0, 0, @Guid, @Now, @Now, @ForeignKey )",
                P( "@Name", name ), P( "@Description", description ), P( "@Number", number ), P( "@Order", order ), P( "@AssignedTo", assignedToAliasId ),
                P( "@Guid", guid ), P( "@Now", now ), P( "@ForeignKey", _foreignKey ) );
        }

        #endregion Phone numbers

        #region Template

        /// <summary>
        /// Adds the logo as a Rock file and the branded template that shows it, the way Rock's template
        /// editor saves a template with an uploaded logo.
        /// </summary>
        private (int Id, string Html) AddTemplate( CommunicationsConfiguration configuration, PeopleLoader people )
        {
            var template = configuration.Template;
            var now = DateTime.Now;
            var logo = _source.Files.ReadBytes( template.Logo );
            var info = Image.Identify( logo );
            var fileTypeId = _db.RequireIdByGuid( "BinaryFileType", DefaultFileTypeGuid );
            var storageTypeId = Convert.ToInt32( _db.Scalar( "SELECT [StorageEntityTypeId] FROM [BinaryFileType] WHERE [Id] = @Id", P( "@Id", fileTypeId ) ) );

            var logoId = ( int ) _db.Scalar( @"
INSERT INTO [BinaryFile] ( [IsTemporary], [IsSystem], [BinaryFileTypeId], [FileName], [MimeType], [StorageEntityTypeId], [FileSize], [Width], [Height],
    [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime], [ContentLastModified] )
OUTPUT INSERTED.[Id]
VALUES ( 0, 0, @FileTypeId, @FileName, 'image/png', @StorageTypeId, @FileSize, @Width, @Height, @Guid, @ForeignKey, @Now, @Now, @Now )",
                P( "@FileTypeId", fileTypeId ), P( "@FileName", Path.GetFileName( template.Logo ) ), P( "@StorageTypeId", storageTypeId ), P( "@FileSize", logo.Length ),
                P( "@Width", info.Width ), P( "@Height", info.Height ), P( "@Guid", template.LogoGuid ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );

            _db.Execute( @"
INSERT INTO [BinaryFileData] ( [Id], [Content], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( @Id, @Content, NEWID(), @ForeignKey, @Now, @Now )",
                P( "@Id", logoId ), P( "@Content", logo ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );

            // Rock's template editor points the logo at GetImage.ashx (CommunicationTemplateHelper.GetUpdatedTemplateHtml).
            var html = _source.Files.ReadText( template.Html );
            if ( !html.Contains( PlaceholderLogoSrc ) )
            {
                throw new InvalidDataException( $"{template.Html} has no logo image with the src {PlaceholderLogoSrc}." );
            }

            html = html.Replace( PlaceholderLogoSrc, $"/GetImage.ashx?id={logoId}" );
            var church = configuration.Senders.Church;
            var author = people.People.TryGetValue( church.Sender, out var sender ) ? sender.AliasId : ( int? ) null;

            var templateId = ( int ) _db.Scalar( @"
INSERT INTO [CommunicationTemplate] ( [Name], [Description], [IsSystem], [FromName], [FromEmail], [Message], [IsActive], [LogoBinaryFileId], [CategoryId],
    [LavaFieldsJson], [CssInliningEnabled], [IsStarter], [Version], [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Description, 0, @FromName, @FromEmail, @Message, 1, @LogoId, @CategoryId,
    @LavaFields, 1, 0, 0, @Guid, @Now, @Now, @Author, @Author, @ForeignKey )",
                P( "@Name", template.Name ), P( "@Description", template.Description ), P( "@FromName", church.FromName ), P( "@FromEmail", church.FromEmail ),
                P( "@Message", html ), P( "@LogoId", logoId ), P( "@CategoryId", _db.RequireIdByGuid( "Category", template.CategoryGuid ) ),
                P( "@LavaFields", JsonSerializer.Serialize( template.LavaFields ) ), P( "@Guid", template.Guid ), P( "@Now", now ), P( "@Author", author ), P( "@ForeignKey", _foreignKey ) );
            return (templateId, html);
        }

        #endregion Template

        #region Lists

        /// <summary>
        /// What the planner needs to know about every adult and student: their campus, email, phone,
        /// involvement, family, and the teams, groups, and prayer team they are on.
        /// </summary>
        private List<ListCandidate> Candidates( PeopleLoader people, Dictionary<int, HashSet<DateTime>> familySundays, Dictionary<string, List<RockGift>> gifts,
            HashSet<int> prayerTeam, int smallGroupTypeId, int servingTypeId, ListMembershipRules rules )
        {
            var yearAgo = _dates.Anchor.AddMonths( -12 );
            var inGroup = Memberships( smallGroupTypeId, leadersOnly: false );
            var leading = Memberships( smallGroupTypeId, leadersOnly: true );
            var serving = Memberships( servingTypeId, leadersOnly: false );

            var candidates = new Dictionary<int, ListCandidate>();
            foreach ( var household in people.Households.Where( h => h.Cast.Kind != "business" ) )
            {
                var sundays = familySundays.GetValueOrDefault( household.FamilyId );
                var cameLastYear = sundays != null && sundays.Any( s => s >= yearAgo );
                DateTime? firstSunday = sundays != null && sundays.Count > 0 ? sundays.Min() : null;
                var hasKid = household.Members.Any( m => !m.IsAdult && m.CanAttend && IsKid( m, rules ) );

                foreach ( var person in household.Members.Where( m => m.CanAttend ) )
                {
                    var isAdult = person.IsAdult && ( !person.BirthDate.HasValue || _dates.AgeOn( person.BirthDate.Value ) >= 18 );
                    var grade = person.GraduationYear.HasValue ? AnchorDates.GradeOn( person.GraduationYear.Value, _dates.Anchor ) : ( int? ) null;
                    if ( !isAdult && !( grade >= rules.StudentGrades.Min && grade <= rules.StudentGrades.Max ) && !serving.ContainsKey( person.PersonId ) )
                    {
                        continue;
                    }

                    var gave = person.GivingId != null && gifts.TryGetValue( person.GivingId, out var unitGifts ) && unitGifts.Any( g => g.When >= yearAgo );
                    var cast = person.Cast;
                    var candidate = candidates.GetValueOrDefault( person.PersonId ) ?? new ListCandidate
                    {
                        PersonId = person.PersonId,
                        Guid = cast.Guid,
                        CampusKey = household.CampusKey,
                        IsAdult = isAdult,
                        Grade = isAdult ? null : grade,
                        Gender = cast.Gender,
                        ConnectionStatus = cast.ConnectionStatus,
                        HasActiveEmail = !string.IsNullOrWhiteSpace( cast.Email ) && cast.EmailIsActive,
                        MayGetMassMessages = !cast.EmailDoNotEmail,
                        CanText = !string.IsNullOrWhiteSpace( cast.Phones?.Mobile ),
                        IsPrayerTeam = prayerTeam.Contains( person.PersonId ),
                        ServingSince = serving.TryGetValue( person.PersonId, out var servedFrom ) ? servedFrom : null,
                        LeadingSince = leading.TryGetValue( person.PersonId, out var ledFrom ) ? ledFrom : null
                    };

                    // A person in two households (a shared child) is involved, and a parent, through either.
                    candidate.IsInvolved |= cameLastYear || gave || inGroup.ContainsKey( person.PersonId );
                    candidate.FamilyCameLastYear |= cameLastYear;
                    candidate.HasKid |= isAdult && hasKid;
                    if ( firstSunday.HasValue && ( !candidate.FirstSunday.HasValue || firstSunday < candidate.FirstSunday ) )
                    {
                        candidate.FirstSunday = firstSunday;
                    }

                    candidates[person.PersonId] = candidate;
                }
            }

            return candidates.Values.OrderBy( c => c.PersonId ).ToList();
        }

        /// <summary>
        /// A child from birth to the kids ministry's last grade.
        /// </summary>
        private bool IsKid( LoadedPerson child, ListMembershipRules rules )
        {
            if ( child.GraduationYear.HasValue )
            {
                return AnchorDates.GradeOn( child.GraduationYear.Value, _dates.Anchor ) <= rules.KidsMaxGrade;
            }

            return child.BirthDate.HasValue && _dates.AgeOn( child.BirthDate.Value ) < 6;
        }

        /// <summary>
        /// Each person's earliest active membership in an active group of the type, read back from the
        /// rows the small group and serving steps wrote. Leaders only, when asked.
        /// </summary>
        private Dictionary<int, DateTime> Memberships( int groupTypeId, bool leadersOnly )
        {
            return _db.Query( @"
SELECT [gm].[PersonId], MIN( ISNULL( [gm].[DateTimeAdded], [gm].[CreatedDateTime] ) ) AS [Joined]
FROM [GroupMember] AS [gm]
INNER JOIN [Group] AS [g] ON [g].[Id] = [gm].[GroupId]
INNER JOIN [GroupTypeRole] AS [r] ON [r].[Id] = [gm].[GroupRoleId]
WHERE [g].[GroupTypeId] = @GroupTypeId
    AND [g].[IsActive] = 1
    AND [g].[IsArchived] = 0
    AND [gm].[GroupMemberStatus] = 1
    AND [gm].[IsArchived] = 0
    AND ( @LeadersOnly = 0 OR [r].[IsLeader] = 1 )
GROUP BY [gm].[PersonId]", P( "@GroupTypeId", groupTypeId ), P( "@LeadersOnly", leadersOnly ) )
                .ToDictionary( r => ( int ) r["PersonId"], r => ( DateTime ) r["Joined"] );
        }

        /// <summary>
        /// Adds every list: one per campus where the list says so. Returns list Ids by key and campus.
        /// </summary>
        private Dictionary<(string ListKey, string CampusKey), int> AddLists( CommunicationListConfiguration configuration, IReadOnlyDictionary<string, int> campusIds )
        {
            var now = DateTime.Now;
            var groupTypeId = _db.RequireIdByGuid( "GroupType", CommunicationListGroupTypeGuid );
            var groupEntityTypeId = _db.RequireIdByGuid( "EntityType", GroupEntityTypeGuid );
            var attributeIds = _db.Query( @"
SELECT [Id], [Key]
FROM [Attribute]
WHERE [EntityTypeId] = @EntityTypeId AND [EntityTypeQualifierColumn] = 'GroupTypeId' AND [EntityTypeQualifierValue] = @GroupTypeId",
                P( "@EntityTypeId", groupEntityTypeId ), P( "@GroupTypeId", groupTypeId.ToString() ) )
                .ToDictionary( r => ( string ) r["Key"], r => ( int ) r["Id"] );
            var categoryAttributeId = attributeIds.TryGetValue( "Category", out var category ) ? category : throw new InvalidOperationException( "Rock's Communication List group type has no Category attribute." );
            var publicNameAttributeId = attributeIds.TryGetValue( "PublicName", out var publicName ) ? publicName : throw new InvalidOperationException( "Rock's Communication List group type has no Public Name attribute." );

            var campusOrder = _source.Campuses.Campuses.Select( c => c.Key ).Where( campusIds.ContainsKey ).ToList();
            var ids = new Dictionary<(string, string), int>();
            var order = 0;
            var values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "IsPersistedValueDirty:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            foreach ( var list in configuration.Lists )
            {
                foreach ( var campusKey in list.Campus ? campusOrder : new List<string> { null } )
                {
                    var guid = campusKey == null
                        ? list.Guid ?? throw new InvalidDataException( $"configuration/communications.json: list {list.Key} has no guid." )
                        : list.Guids.TryGetValue( campusKey, out var campusGuid ) ? campusGuid : throw new InvalidDataException( $"configuration/communications.json: list {list.Key} has no guid for {campusKey}." );
                    var id = ( int ) _db.Scalar( @"
INSERT INTO [Group] ( [IsSystem], [GroupTypeId], [CampusId], [Name], [Description], [IsSecurityRole], [IsActive], [IsPublic], [Order], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @GroupTypeId, @CampusId, @Name, @Description, 0, 1, @IsPublic, @Order, @Guid, @ForeignKey, @Now, @Now )",
                        P( "@GroupTypeId", groupTypeId ), P( "@CampusId", campusKey == null ? null : ( object ) campusIds[campusKey] ), P( "@Name", ListName( list, campusKey ) ),
                        P( "@Description", list.Description ), P( "@IsPublic", list.IsPublic ), P( "@Order", order++ ), P( "@Guid", guid ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );
                    ids[(list.Key, campusKey)] = id;

                    // A public list shows on Rock's email preference page under the Public category.
                    if ( list.IsPublic )
                    {
                        values.Rows.Add( false, categoryAttributeId, id, configuration.PublicCategoryGuid.ToString().ToUpperInvariant(), true, Guid.NewGuid(), _foreignKey, now, now );
                    }

                    values.Rows.Add( false, publicNameAttributeId, id, CampusText( list.PublicName, campusKey ), true, Guid.NewGuid(), _foreignKey, now, now );
                }
            }

            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );
            return ids;
        }

        /// <summary>
        /// Writes every list member as an active recipient with their preference on the list.
        /// </summary>
        private void WriteMembers( List<PlannedListMember> plan, Dictionary<(string ListKey, string CampusKey), int> listIds )
        {
            var groupTypeId = _db.RequireIdByGuid( "GroupType", CommunicationListGroupTypeGuid );
            var roleId = _db.RequireIdByGuid( "GroupTypeRole", CommunicationListRecipientRoleGuid );
            var nextId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [GroupMember]" ) );
            var random = new SeededRandom( $"{_seed}:communication-lists:guids" );
            var members = TableFactory.Create( "Id:int", "IsSystem:bool", "GroupId:int", "GroupTypeId:int", "PersonId:int", "GroupRoleId:int", "GroupMemberStatus:int", "DateTimeAdded:date",
                "CommunicationPreference:int", "InactiveDateTime:date", "Note:string", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            // A member who left from a list email is Inactive, with the note Rock's email preference page writes.
            foreach ( var member in plan )
            {
                var left = member.UnsubscribedAt;
                members.Rows.Add( nextId++, false, listIds[(member.ListKey, member.CampusKey)], groupTypeId, member.PersonId, roleId, left.HasValue ? 0 : 1, member.Joined,
                    member.IsSms ? SmsPreference : EmailPreference, left.HasValue ? left.Value : ( object ) DBNull.Value, left.HasValue ? "Unsubscribed" : ( object ) DBNull.Value,
                    random.NextGuid(), _foreignKey, member.Joined, left ?? member.Joined );
            }

            _db.BulkInsert( "GroupMember", members, keepIdentity: true );
        }

        private string ListName( CommunicationListDefinition list, string campusKey ) => CampusText( list.Name, campusKey );

        private string CampusText( string text, string campusKey )
        {
            return campusKey == null ? text : text.Replace( "{campus}", _source.Campuses.Campuses.First( c => c.Key == campusKey ).Name );
        }

        #endregion Lists
    }
}
