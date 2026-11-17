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

using System.Text.Json;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/16/26 - CLAUDE

        Each segment is written as Rock's segment editor saves one built on a data view, with the editor's default
        daily refresh, and with the people Rock's Update Personalization Data job would put in it on install day: the
        primary alias of everyone in its view (PersonalizationSegmentService.UpdatePersonAliasPersonalizationDataForSegment).
        Its last refresh is now, so the job's next run is tomorrow, and verify runs Rock's own update on every segment and
        expects it to add and remove nobody. An adaptation with no segments shows to everyone, so an adaptation whose
        segment could not be written is skipped, never written without it.

        Reason: Issue 6. The loader fills derived rows the way Rock's job leaves them, as group sync does (decision 187).
    */

    /// <summary>
    /// Writes the church's personalization segments and adaptive messages from configuration/adaptive-messages.json, with
    /// each adaptation's text from content/adaptive-messages/messages.json.
    /// </summary>
    internal sealed partial class AdaptiveMessagesLoader
    {
        /// <summary>
        /// Rock's adaptation attributes (AdaptiveMessageAdaptation), the same in Rock 20 and Rock 21.
        /// </summary>
        private static readonly Guid CallToActionAttributeGuid = new Guid( "9E67B39B-C95C-464B-AC7B-CB191834EF85" );
        private static readonly Guid CallToActionLinkAttributeGuid = new Guid( "FF9A62A6-3F19-458C-9527-9A3274652BCC" );
        private static readonly Guid SummaryImageAttributeGuid = new Guid( "A927602E-DC72-4CE5-B9B6-D6F77086AB6D" );
        private static readonly Guid SummaryAttributeGuid = new Guid( "611C3F9D-67BA-496E-9533-2D07B0AD9733" );
        private static readonly Guid DetailsAttributeGuid = new Guid( "B55E8E55-1021-4A9E-8D05-EA9F52AA5C98" );

        /// <summary>
        /// The Editor.js version Rock's structured content editor writes into each value it saves.
        /// </summary>
        private const string EditorVersion = "2.31.0-rc.7";

        /// <summary>
        /// The additional filters Rock's segment editor saves for a segment that uses only its data view: no session, page
        /// view, or interaction filters, each group set to All. Rock's segment update reads it without a null check, so a
        /// segment without it stops Rock's Update Personalization Data job.
        /// </summary>
        private const string NoAdditionalFilters = "{\"SessionFilterExpressionType\":1,\"SessionSegmentFilters\":[],\"PageViewFilterExpressionType\":1,\"PageViewSegmentFilters\":[],\"InteractionFilterExpressionType\":1,\"InteractionSegmentFilters\":[]}";

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public AdaptiveMessagesLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// Writes every segment it can, with its people, then every message with the adaptations whose segments were written.
        /// </summary>
        public void Load()
        {
            var configuration = _source.AdaptiveMessages;
            if ( configuration == null || _source.Categories == null )
            {
                _db.Log.Write( "  Adaptive messages: skipped (this package has no configuration/adaptive-messages.json)" );
                return;
            }

            var now = DateTime.Now;
            var security = new SecurityRuleWriter( _db, _source.SecurityRoles, _foreignKey );
            var segmentIds = WriteSegments( configuration, security, now );

            var resultsPage = configuration.ResultsPage;
            if ( resultsPage != null )
            {
                security.Write( "Rock.Model.Page", _db.RequireIdByGuid( "Page", resultsPage.Guid ), resultsPage.Security, now, $"{SampleDataSource.AdaptiveMessagesConfigurationPath}: page {resultsPage.Name}" );
            }

            var (messages, adaptations) = WriteMessages( configuration, segmentIds, now );
            _db.Log.Write( $"  Adaptive messages: {messages} messages with {adaptations} adaptations" );
        }

        /// <summary>
        /// Writes each segment whose data view this load wrote, its security, and the people in it. Returns each written
        /// segment's Id by key.
        /// </summary>
        private Dictionary<string, int> WriteSegments( AdaptiveMessagesConfiguration configuration, SecurityRuleWriter security, DateTime now )
        {
            var views = _source.DataViews();
            var evaluator = new DataViewEvaluator( _db, _dates.Anchor, views, f => f.Steps != null ? ReportingLoader.StepsSelection( _source.Steps, f.Steps ) : null );

            // Rock puts each person in a segment by their primary alias.
            var primaryAliases = _db.Query( "SELECT [PersonId], MIN( [Id] ) AS [AliasId] FROM [PersonAlias] WHERE [AliasPersonId] = [PersonId] GROUP BY [PersonId]" )
                .ToDictionary( r => ( int ) r["PersonId"], r => ( int ) r["AliasId"] );
            var rows = TableFactory.Create( "PersonAliasId:int", "PersonalizationType:int", "PersonalizationEntityId:int" );
            var ids = new Dictionary<string, int>();
            var counts = new List<string>();
            foreach ( var segment in configuration.Segments )
            {
                var view = views.TryGetValue( segment.DataView, out var found ) ? found
                    : throw new InvalidDataException( $"{SampleDataSource.AdaptiveMessagesConfigurationPath}: segment {segment.Key} uses data view {segment.DataView}, which is not a data view in any configuration file." );
                var viewId = _db.IdByGuid( "DataView", view.Guid );
                if ( viewId == null )
                {
                    _db.Log.Write( $"    Skipped segment {segment.Key}: its data view {segment.DataView} was not written" );
                    continue;
                }

                var id = ( int ) _db.Scalar( @"
INSERT INTO [PersonalizationSegment] ( [Name], [SegmentKey], [IsActive], [FilterDataViewId], [AdditionalFilterJson], [IsDirty], [Description], [PersistedScheduleIntervalMinutes],
    [PersistedLastRefreshDateTime], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Key, 1, @ViewId, @Filters, 0, @Description, @Interval, @Now, @Guid, @ForeignKey, @Now, @Now )",
                    P( "@Name", segment.Name ), P( "@Key", segment.Key ), P( "@ViewId", viewId ), P( "@Filters", NoAdditionalFilters ), P( "@Description", segment.Description ),
                    P( "@Interval", configuration.SegmentRefreshMinutes ), P( "@Now", now ), P( "@Guid", segment.Guid ), P( "@ForeignKey", _foreignKey ) );
                security.Write( "Rock.Model.PersonalizationSegment", id, segment.Security, now, $"{SampleDataSource.AdaptiveMessagesConfigurationPath}: segment {segment.Key}" );
                ids[segment.Key] = id;

                var people = evaluator.EvaluateView( view ).Keys.Where( primaryAliases.ContainsKey ).Select( p => primaryAliases[p] ).Distinct().OrderBy( a => a ).ToList();
                foreach ( var aliasId in people )
                {
                    rows.Rows.Add( aliasId, 0, id );
                }

                counts.Add( $"{segment.Name} {people.Count:N0}" );
            }

            // PersonAliasPersonalization has no ForeignKey column; the segment it points at carries the load's.
            _db.BulkInsert( "PersonAliasPersonalization", rows, keepIdentity: false );
            _db.Log.Write( $"  Personalization segments: {ids.Count} ({string.Join( ", ", counts )})" );
            return ids;
        }

        /// <summary>
        /// Writes each message that has an adaptation this load can write, in the church's category, with its adaptations
        /// in order, their segments, and their text. Returns how many messages and adaptations.
        /// </summary>
        private (int Messages, int Adaptations) WriteMessages( AdaptiveMessagesConfiguration configuration, Dictionary<string, int> segmentIds, DateTime now )
        {
            var text = _source.ReadAdaptiveMessageText();
            var categoryId = _db.RequireIdByGuid( "Category", _source.Categories.Get( configuration.Category ).Guid );
            var today = _dates.Anchor.Date;

            // Pick what loads first, so every image is written in one pass.
            var plans = new List<(AdaptiveMessageDefinition Message, (DateTime? Start, DateTime? End) Dates, List<(AdaptationDefinition Adaptation, AdaptationText Text, (DateTime? Start, DateTime? End) Dates)> Adaptations)>();
            foreach ( var message in configuration.Messages )
            {
                var messageDates = Dates( message.Dates, today );
                if ( messageDates == null )
                {
                    _db.Log.Write( $"    Skipped message {message.Key}: this load has no {message.Dates.Rule} dates" );
                    continue;
                }

                var adaptations = new List<(AdaptationDefinition, AdaptationText, (DateTime?, DateTime?))>();
                foreach ( var adaptation in message.Adaptations )
                {
                    var adaptationText = text.Messages.TryGetValue( message.Key, out var byKey ) && byKey.TryGetValue( adaptation.Key, out var found ) ? found
                        : throw new InvalidDataException( $"{SampleDataSource.AdaptiveMessageTextPath} has no text for {message.Key} {adaptation.Key}." );
                    var missing = adaptation.Segments.FirstOrDefault( s => !segmentIds.ContainsKey( s ) );
                    var adaptationDates = Dates( adaptation.Dates, today );
                    if ( missing != null || adaptationDates == null )
                    {
                        _db.Log.Write( $"    Skipped adaptation {message.Key} {adaptation.Key}: " + ( missing != null ? $"its segment {missing} was not written" : $"this load has no {adaptation.Dates.Rule} dates" ) );
                        continue;
                    }

                    adaptations.Add( (adaptation, adaptationText, adaptationDates.Value) );
                }

                if ( adaptations.Count == 0 )
                {
                    _db.Log.Write( $"    Skipped message {message.Key}: none of its adaptations could be written" );
                    continue;
                }

                plans.Add( (message, messageDates.Value, adaptations) );
            }

            var withImages = plans.SelectMany( p => p.Adaptations ).Where( a => a.Text.Image != null ).ToList();
            var imageGuids = new PhotoLoader( _db, _source, _foreignKey, _seed ).LoadEventImageCopies( withImages.Select( a => a.Text.Image ).ToList() );
            var images = withImages.Select( ( a, i ) => (a.Adaptation.Guid, imageGuids[i]) ).ToDictionary( p => p.Guid, p => p.Item2 );

            var attributeIds = new Dictionary<Guid, int>();
            int AttributeId( Guid guid ) => attributeIds.TryGetValue( guid, out var id ) ? id : attributeIds[guid] = _db.RequireIdByGuid( "Attribute", guid );
            var values = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "IsPersistedValueDirty:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var adaptationCount = 0;
            for ( var order = 0; order < plans.Count; order++ )
            {
                var (message, messageDates, adaptations) = plans[order];
                var messageId = ( int ) _db.Scalar( @"
INSERT INTO [AdaptiveMessage] ( [Name], [Description], [Key], [IsActive], [StartDate], [EndDate], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Description, @Key, 1, @Start, @End, @Guid, @ForeignKey, @Now, @Now )",
                    P( "@Name", message.Name ), P( "@Description", message.Description ), P( "@Key", message.Key ), P( "@Start", messageDates.Start ), P( "@End", messageDates.End ),
                    P( "@Guid", message.Guid ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );

                // Rock's message editor files a message in each category it picks, in the order Rock lists them.
                _db.Execute( @"
INSERT INTO [AdaptiveMessageCategory] ( [AdaptiveMessageId], [CategoryId], [Order], [Guid], [ForeignKey] )
VALUES ( @MessageId, @CategoryId, @Order, @Guid, @ForeignKey )",
                    P( "@MessageId", messageId ), P( "@CategoryId", categoryId ), P( "@Order", order ),
                    P( "@Guid", SeededRandom.StableGuid( $"adaptive-message-category:{message.Guid}" ) ), P( "@ForeignKey", _foreignKey ) );

                for ( var position = 0; position < adaptations.Count; position++ )
                {
                    var (adaptation, adaptationText, dates) = adaptations[position];
                    var adaptationId = ( int ) _db.Scalar( @"
INSERT INTO [AdaptiveMessageAdaptation] ( [Name], [IsActive], [Order], [ViewSaturationCount], [ViewSaturationInDays], [AdaptiveMessageId], [StartDate], [EndDate],
    [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, 1, @Order, @Views, @Days, @MessageId, @Start, @End, @Guid, @ForeignKey, @Now, @Now )",
                        P( "@Name", adaptation.Name ), P( "@Order", position ), P( "@Views", adaptation.Saturation?.Views ), P( "@Days", adaptation.Saturation?.Days ),
                        P( "@MessageId", messageId ), P( "@Start", dates.Start ), P( "@End", dates.End ), P( "@Guid", adaptation.Guid ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );

                    foreach ( var segment in adaptation.Segments )
                    {
                        _db.Execute( @"
INSERT INTO [AdaptiveMessageAdaptationSegment] ( [PersonalizationSegmentId], [AdaptiveMessageAdaptationId], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( @SegmentId, @AdaptationId, @Guid, @ForeignKey, @Now, @Now )",
                            P( "@SegmentId", segmentIds[segment] ), P( "@AdaptationId", adaptationId ),
                            P( "@Guid", SeededRandom.StableGuid( $"adaptation-segment:{adaptation.Guid}:{segment}" ) ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );
                    }

                    void Value( Guid attribute, string value )
                    {
                        if ( !string.IsNullOrEmpty( value ) )
                        {
                            values.Rows.Add( false, AttributeId( attribute ), adaptationId, value, true, SeededRandom.StableGuid( $"adaptation-value:{adaptation.Guid}:{attribute}" ), _foreignKey, now, now );
                        }
                    }

                    Value( CallToActionAttributeGuid, adaptationText.CallToAction );
                    Value( CallToActionLinkAttributeGuid, Link( configuration, adaptationText.Link ) );
                    Value( SummaryAttributeGuid, adaptationText.Summary );
                    Value( DetailsAttributeGuid, StructuredContent( adaptation, adaptationText.Details, now ) );
                    Value( SummaryImageAttributeGuid, images.TryGetValue( adaptation.Guid, out var image ) ? image.ToString() : null );
                    adaptationCount++;
                }
            }

            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );
            return (plans.Count, adaptationCount);
        }

        /*
            11/16/26 - CLAUDE

            Rock shows a message or an adaptation while its start date is on or before now and its end date is on or after
            now (AdaptiveMessageBlock.IsCurrentlyActive). Dates are stored without a time, so the end date is midnight and
            the item stops showing as that day begins. Camp and the campaign show through their last day, so their end
            is the next day. The baptism invitation ends as baptism Sunday begins.

            Reason: The dates must read as the church would set them in Rock's editor, and show on the days the issue asks.
        */

        /// <summary>
        /// The start and end a dates rule gives on the install day, (null, null) for no rule, or null when this load cannot
        /// tell, such as camp dates in a load without registrations.
        /// </summary>
        private (DateTime? Start, DateTime? End)? Dates( AdaptiveMessageDates rule, DateTime today )
        {
            switch ( rule?.Rule )
            {
                case null:
                    return (null, null);

                case "campaign":
                    var fund = _source.Campaign == null ? null : _source.Accounts.Accounts.FirstOrDefault( a => a.Name == _source.Campaign.Fund );
                    var account = fund == null ? null : _db.Query( "SELECT [StartDate], [EndDate] FROM [FinancialAccount] WHERE [Guid] = @Guid", P( "@Guid", fund.Guid ) ).FirstOrDefault();
                    return account?["StartDate"] is DateTime start && account["EndDate"] is DateTime end ? (start.Date, end.Date.AddDays( 1 )) : null;

                case "before-baptism-sunday":
                    var baptism = _source.Steps?.StepTypes.Single( t => t.Source?.Kind == "baptism" ).Source;
                    if ( baptism == null )
                    {
                        return null;
                    }

                    var sunday = Enumerable.Range( today.Year, 2 ).SelectMany( y => new[] { SundayCalendar.Easter( y ), baptism.SummerSunday.In( y ) } ).Where( d => d > today ).Min();
                    return (sunday.AddDays( -rule.Days ), sunday);

                case "camp-registration":
                    // Camp registration is rolling, so the next camp's instances are the ones whose registration has not closed.
                    var instances = _db.Query( @"
SELECT [ri].[StartDateTime], [ri].[EndDateTime]
FROM [RegistrationInstance] AS [ri]
INNER JOIN [RegistrationTemplate] AS [rt] ON [rt].[Id] = [ri].[RegistrationTemplateId]
WHERE [rt].[Guid] = @Template AND [ri].[EndDateTime] >= @Today AND [ri].[StartDateTime] IS NOT NULL",
                        P( "@Template", SeededRandom.StableGuid( "registration-template:student-camp" ) ), P( "@Today", today ) )
                        .Select( r => (Start: ( DateTime ) r["StartDateTime"], End: ( DateTime ) r["EndDateTime"]) )
                        .ToList();
                    if ( instances.Count == 0 )
                    {
                        return null;
                    }

                    var nextYear = instances.Min( i => i.End ).Year;
                    var next = instances.Where( i => i.End.Year == nextYear ).ToList();
                    return (next.Min( i => i.Start ).Date, next.Max( i => i.End ).Date.AddDays( 1 ));

                default:
                    throw new InvalidDataException( $"{SampleDataSource.AdaptiveMessagesConfigurationPath}: '{rule.Rule}' is not a dates rule the loader knows." );
            }
        }

        /// <summary>
        /// A call to action link: an External Website route as it is, or page:key as Rock's /page/Id link to that page.
        /// </summary>
        private string Link( AdaptiveMessagesConfiguration configuration, string link )
        {
            if ( link == null || !link.StartsWith( "page:", StringComparison.Ordinal ) )
            {
                return link;
            }

            var key = link.Substring( "page:".Length );
            var page = configuration.Pages.TryGetValue( key, out var found ) ? found
                : throw new InvalidDataException( $"{SampleDataSource.AdaptiveMessageTextPath} links to page {key}, which is not in the configuration's pages." );
            return $"/page/{_db.RequireIdByGuid( "Page", page.Guid )}";
        }

        /// <summary>
        /// The Details value as Rock's structured content editor saves it: one paragraph block for each paragraph.
        /// </summary>
        private static string StructuredContent( AdaptationDefinition adaptation, List<string> paragraphs, DateTime now )
        {
            if ( paragraphs == null || paragraphs.Count == 0 )
            {
                return null;
            }

            var random = new SeededRandom( $"adaptation-details:{adaptation.Guid}" );
            const string IdCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-";
            string BlockId() => new string( Enumerable.Range( 0, 10 ).Select( _ => IdCharacters[random.Between( 0, IdCharacters.Length - 1 )] ).ToArray() );
            return JsonSerializer.Serialize( new
            {
                time = new DateTimeOffset( now ).ToUnixTimeMilliseconds(),
                blocks = paragraphs.Select( p => new { id = BlockId(), type = "paragraph", data = new { text = p } } ).ToList(),
                version = EditorVersion
            } );
        }

        /// <summary>
        /// The adaptation of a message Rock's adaptivemessage command shows a person on a day, before any views: the first
        /// active adaptation, by order then name, whose segments include the person's primary alias, or that has no segments.
        /// Null when none fits or the message is not active. This is the rule in AdaptiveMessageBlock.OnRender.
        /// </summary>
        /// <param name="person">The person's Guid, as a SQL parameter value (CastPersonGuids.Value).</param>
        internal static Guid? ShownAdaptation( RockDatabase db, Guid message, object person, DateTime day )
        {
            var rows = db.Query( @"
SELECT [a].[Guid], [a].[Name], [a].[Order],
    ( SELECT COUNT(*) FROM [AdaptiveMessageAdaptationSegment] AS [s] WHERE [s].[AdaptiveMessageAdaptationId] = [a].[Id] ) AS [Segments],
    ( SELECT COUNT(*)
      FROM [AdaptiveMessageAdaptationSegment] AS [s]
      INNER JOIN [PersonalizationSegment] AS [ps] ON [ps].[Id] = [s].[PersonalizationSegmentId] AND [ps].[IsActive] = 1
      INNER JOIN [PersonAliasPersonalization] AS [pap] ON [pap].[PersonalizationEntityId] = [ps].[Id] AND [pap].[PersonalizationType] = 0
      WHERE [s].[AdaptiveMessageAdaptationId] = [a].[Id] AND [pap].[PersonAliasId] = [p].[PrimaryAliasId] ) AS [Matches]
FROM [AdaptiveMessageAdaptation] AS [a]
INNER JOIN [AdaptiveMessage] AS [m] ON [m].[Id] = [a].[AdaptiveMessageId]
CROSS JOIN [Person] AS [p]
WHERE [m].[Guid] = @Message AND [p].[Guid] = @Person
    AND [m].[IsActive] = 1 AND ( [m].[StartDate] IS NULL OR [m].[StartDate] <= @Day ) AND ( [m].[EndDate] IS NULL OR [m].[EndDate] > @Day )
    AND [a].[IsActive] = 1 AND ( [a].[StartDate] IS NULL OR [a].[StartDate] <= @Day ) AND ( [a].[EndDate] IS NULL OR [a].[EndDate] > @Day )",
                P( "@Message", message ), P( "@Person", person ), P( "@Day", day.Date ) );
            return rows.OrderBy( r => ( int ) r["Order"] ).ThenBy( r => ( string ) r["Name"], StringComparer.Ordinal )
                .Where( r => Convert.ToInt32( r["Segments"] ) == 0 || Convert.ToInt32( r["Matches"] ) > 0 )
                .Select( r => ( Guid? ) r["Guid"] )
                .FirstOrDefault();
        }
    }
}
