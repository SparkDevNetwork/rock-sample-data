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
    /// <summary>
    /// Writes the bookmarks behind Rock's bookmark icon from configuration/personal-links.json (issue 46): shared sections
    /// secured by role, a personal section for named staff and each loaded campus pastor, their links, and each owner's
    /// section order.
    /// </summary>
    internal sealed class PersonalLinksLoader
    {
        private static readonly string[] PrivateActions = { "View", "Edit", "Administrate" };

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;
        private readonly SecurityRuleWriter _security;
        private readonly List<string> _skipped = new List<string>();
        private int _links;

        public PersonalLinksLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
            _security = new SecurityRuleWriter( db, source.SecurityRoles, foreignKey );
        }

        public bool HasConfiguration => _source.PersonalLinks != null;

        /// <param name="parts">The parts this load wrote. A link to a part it left out is skipped.</param>
        /// <param name="campusPastors">Each loaded campus's campus pastor, who gets a section for their campus.</param>
        /// <param name="campusIds">Each loaded campus's Id, by campus key.</param>
        public string Load( PeopleLoader people, IReadOnlyCollection<SampleDataPart> parts, IReadOnlyDictionary<string, LoadedPerson> campusPastors, IReadOnlyDictionary<string, int> campusIds )
        {
            var configuration = _source.PersonalLinks;
            var creator = Person( people, configuration.CreatedBy, "createdBy" );
            var shared = new List<(int Id, Guid Guid)>();
            var sharedDone = DateTime.MinValue;
            foreach ( var section in configuration.SharedSections )
            {
                var links = Links( section.Links, parts, null, campusIds );
                if ( links.Count == 0 )
                {
                    continue;
                }

                var created = Created( section.Guid, section.CreatedDaysAgo );
                var id = WriteSection( section.Name, section.Guid, null, creator.AliasId, created );
                var rules = new List<CategorySecurityRule> { section.Security };
                rules.AddRange( new[] { "Edit", "Administrate" }.Select( action => new CategorySecurityRule { Action = action, Allow = configuration.EditorRoles } ) );
                _security.Write( "Rock.Model.PersonalLinkSection", id, rules, created, $"{SampleDataSource.PersonalLinksConfigurationPath}: shared section '{section.Key}'" );
                WriteLinks( id, section.Guid, links, null, creator.AliasId, created );
                shared.Add( (id, section.Guid) );
                sharedDone = Later( sharedDone, created.AddMinutes( links.Count ) );
            }

            // Each owner's own sections, by their primary alias.
            var owned = new Dictionary<int, LinkOwner>();
            foreach ( var section in configuration.PersonalSections )
            {
                if ( !people.People.TryGetValue( section.Owner, out var owner ) )
                {
                    continue;
                }

                WritePersonal( section.Name, section.Guid, owner, Links( section.Links, parts, null, campusIds ), Created( section.Guid, section.CreatedDaysAgo ), owned );
            }

            var pastorSection = configuration.CampusPastorSection;
            foreach ( var (campusKey, pastor) in campusPastors.OrderBy( p => p.Key, StringComparer.Ordinal ) )
            {
                var guid = pastorSection.Guids.TryGetValue( campusKey, out var campusGuid ) ? campusGuid
                    : throw new InvalidDataException( $"{SampleDataSource.PersonalLinksConfigurationPath}: campusPastorSection has no Guid for {campusKey}, a campus in this dataset." );
                WritePersonal( pastorSection.Name, guid, pastor, Links( pastorSection.Links, parts, campusKey, campusIds ), Created( guid, pastorSection.CreatedDaysAgo ), owned );
            }

            var orders = WriteSectionOrders( owned, shared, sharedDone );
            var skippedText = _skipped.Count == 0 ? string.Empty : $", {_skipped.Count} links skipped ({string.Join( "; ", _skipped.Distinct() )})";
            return $"{shared.Count} shared and {owned.Values.Sum( o => o.Sections.Count )} personal sections, {_links} links, {orders} section orders{skippedText}";
        }

        /// <summary>
        /// A private section for its owner and its links, as Rock's bookmark menu saves them.
        /// </summary>
        private void WritePersonal( string name, Guid guid, LoadedPerson owner, List<(string Name, string Url)> links, DateTime created, Dictionary<int, LinkOwner> owned )
        {
            if ( links.Count == 0 )
            {
                return;
            }

            var id = WriteSection( name, guid, owner.AliasId, owner.AliasId, created );
            _security.WritePrivate( "Rock.Model.PersonalLinkSection", id, owner.AliasId, PrivateActions, created );
            WriteLinks( id, guid, links, owner.AliasId, owner.AliasId, created );
            if ( !owned.TryGetValue( owner.AliasId, out var mine ) )
            {
                mine = owned[owner.AliasId] = new LinkOwner { Person = owner };
            }

            mine.Sections.Add( (id, guid) );
            mine.Arranged = Later( mine.Arranged, created.AddMinutes( links.Count ) );
        }

        private int WriteSection( string name, Guid guid, int? ownerAliasId, int creatorAliasId, DateTime created )
        {
            return ( int ) _db.Scalar( @"
INSERT INTO [PersonalLinkSection] ( [PersonAliasId], [Name], [IsShared], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId] )
OUTPUT INSERTED.[Id]
VALUES ( @OwnerAliasId, @Name, @IsShared, @Guid, @ForeignKey, @Created, @Created, @CreatorAliasId, @CreatorAliasId )",
                P( "@OwnerAliasId", ownerAliasId ), P( "@Name", name ), P( "@IsShared", ownerAliasId == null ), P( "@Guid", guid ), P( "@ForeignKey", _foreignKey ),
                P( "@Created", created ), P( "@CreatorAliasId", creatorAliasId ) );
        }

        /*
            11/17/26 - CLAUDE

            Rock's link list adds each new link after the last one in its section, at the highest order plus one, so the
            first is 1 (PersonalLinkList.SaveLink). A link in a shared section has no owner, and one in a personal section
            belongs to its owner, as Rock's save sets it. Each row's ModifiedDateTime is set, because Rock builds the
            version its bookmark menu caches by from the newest ModifiedDateTime of the shared sections and links, and of
            each person's own sections, links, and section orders (PersonalLinkService.GetPersonalLinksModificationHash).
            With none set, Rock would use midnight today, the same version as a database with no links, and a browser that
            cached an empty menu earlier that day would keep showing it.

            Reason: The bookmark menu opens filled on install day, with no extra step.
        */
        private void WriteLinks( int sectionId, Guid sectionGuid, List<(string Name, string Url)> links, int? ownerAliasId, int creatorAliasId, DateTime created )
        {
            for ( var i = 0; i < links.Count; i++ )
            {
                var added = created.AddMinutes( i + 1 );
                _db.Execute( @"
INSERT INTO [PersonalLink] ( [PersonAliasId], [Name], [Url], [SectionId], [Order], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId] )
VALUES ( @OwnerAliasId, @Name, @Url, @SectionId, @Order, @Guid, @ForeignKey, @Added, @Added, @CreatorAliasId, @CreatorAliasId )",
                    P( "@OwnerAliasId", ownerAliasId ), P( "@Name", links[i].Name ), P( "@Url", links[i].Url ), P( "@SectionId", sectionId ), P( "@Order", i + 1 ),
                    P( "@Guid", SeededRandom.StableGuid( $"personal-link:{sectionGuid}:{links[i].Name}" ) ), P( "@ForeignKey", _foreignKey ),
                    P( "@Added", added ), P( "@CreatorAliasId", creatorAliasId ) );
                _links++;
            }
        }

        /*
            11/17/26 - CLAUDE

            Rock orders a person's sections by their PersonalLinkSectionOrder rows, then by name. The first time a person
            opens their links page, Rock adds an order for every shared section and every section of their own
            (PersonalLinkService.AddMissingPersonalLinkSectionOrders). So each owner of a personal section has an order for
            all of them: their own sections first, then the shared ones in the file's order, numbered from 0, as Rock leaves
            them after a person drags their sections into place. Rock then finds none missing. A person with no section of
            their own has no orders, as in Rock, and sees the shared sections by name.

            Reason: Each owner's menu opens in their order, and Rock adds no rows when they open their links.
        */
        private int WriteSectionOrders( Dictionary<int, LinkOwner> owned, List<(int Id, Guid Guid)> shared, DateTime sharedDone )
        {
            var count = 0;
            foreach ( var (aliasId, mine) in owned )
            {
                // The owner arranges their sections once every one of them exists.
                var arranged = Later( mine.Arranged, sharedDone ).AddMinutes( 1 );
                var order = 0;
                foreach ( var (sectionId, sectionGuid) in mine.Sections.Concat( shared ) )
                {
                    _db.Execute( @"
INSERT INTO [PersonalLinkSectionOrder] ( [PersonAliasId], [SectionId], [Order], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId] )
VALUES ( @AliasId, @SectionId, @Order, @Guid, @ForeignKey, @Arranged, @Arranged, @AliasId, @AliasId )",
                        P( "@AliasId", aliasId ), P( "@SectionId", sectionId ), P( "@Order", order++ ),
                        P( "@Guid", SeededRandom.StableGuid( $"personal-link-order:{mine.Person.Cast.Guid}:{sectionGuid}" ) ), P( "@ForeignKey", _foreignKey ), P( "@Arranged", arranged ) );
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Each link's name and URL, leaving out a link whose part this load left out or whose target it did not write.
        /// </summary>
        /// <param name="campusKey">The campus a campus pastor's section is for, or null.</param>
        private List<(string Name, string Url)> Links( List<PersonalLinkDefinition> links, IReadOnlyCollection<SampleDataPart> parts, string campusKey, IReadOnlyDictionary<string, int> campusIds )
        {
            var campusName = campusKey == null ? null : _source.Campuses.Campuses.First( c => c.Key == campusKey ).Name;
            var written = new List<(string, string)>();
            foreach ( var link in links )
            {
                var name = campusName == null ? link.Name : link.Name.Replace( "{campus}", campusName );
                if ( link.Part != null )
                {
                    if ( !Enum.TryParse<SampleDataPart>( link.Part, out var part ) )
                    {
                        throw new InvalidDataException( $"{SampleDataSource.PersonalLinksConfigurationPath}: link {link.Name} names part {link.Part}, which is not a load part." );
                    }

                    if ( !parts.Contains( part ) )
                    {
                        _skipped.Add( $"this load left out {part}" );
                        continue;
                    }
                }

                var url = $"/page/{_db.RequireIdByGuid( "Page", link.Page )}";
                if ( link.Parameter != null )
                {
                    var targetId = TargetId( link, campusKey, campusIds );
                    if ( targetId == null )
                    {
                        _skipped.Add( $"{name}'s {link.Target.Kind} was not written" );
                        continue;
                    }

                    url += $"?{link.Parameter}={targetId}";
                }

                written.Add( (name, url) );
            }

            return written;
        }

        /// <summary>
        /// The Id of the row a link's page parameter names, or null when this load did not write it.
        /// </summary>
        private int? TargetId( PersonalLinkDefinition link, string campusKey, IReadOnlyDictionary<string, int> campusIds )
        {
            var target = link.Target ?? throw new InvalidDataException( $"{SampleDataSource.PersonalLinksConfigurationPath}: link {link.Name} has a parameter but no target." );
            string Campus() => campusKey ?? throw new InvalidDataException( $"{SampleDataSource.PersonalLinksConfigurationPath}: link {link.Name} needs a campus, so it belongs in campusPastorSection." );
            switch ( target.Kind )
            {
                case "report":
                    return _db.IdByGuid( "Report", _source.Reporting.Reports.FirstOrDefault( r => r.Key == target.Key )?.Guid ?? Unknown( link ) );
                case "dataView":
                    return _db.IdByGuid( "DataView", _source.DataViews().TryGetValue( target.Key, out var view ) ? view.Guid : Unknown( link ) );
                case "category":
                    return _db.IdByGuid( "Category", _source.Categories.Get( target.Key ).Guid );
                case "stepProgram":
                    return _db.IdByGuid( "StepProgram", target.Guid ?? Unknown( link ) );
                case "eventCalendar":
                    return _db.IdByGuid( "EventCalendar", target.Guid ?? Unknown( link ) );
                case "campaignFund":
                    return _db.IdByGuid( "FinancialAccount", _source.Accounts.Accounts.First( a => a.Name == _source.Campaign.Fund ).Guid );
                case "campus":
                    return campusIds[Campus()];
                case "campusList":
                    var list = _source.Communications?.Lists.Lists.FirstOrDefault( l => l.Key == target.Key );
                    return _db.IdByGuid( "Group", list != null && list.Guids.TryGetValue( Campus(), out var listGuid ) ? listGuid : Unknown( link ) );
                case "campusSmallGroups":
                    return _db.IdByGuid( "Group", _source.SmallGroups.Parents.Campuses[Campus()] );
                default:
                    throw new InvalidDataException( $"{SampleDataSource.PersonalLinksConfigurationPath}: link {link.Name} has target kind '{target.Kind}', which the loader does not know." );
            }
        }

        private static DateTime Later( DateTime a, DateTime b ) => a > b ? a : b;

        private static Guid Unknown( PersonalLinkDefinition link )
        {
            throw new InvalidDataException( $"{SampleDataSource.PersonalLinksConfigurationPath}: link {link.Name} names {link.Target.Kind} {link.Target.Key ?? link.Target.Guid?.ToString()}, which no configuration file has." );
        }

        private static LoadedPerson Person( PeopleLoader people, Guid guid, string field )
        {
            return people.People.TryGetValue( guid, out var person ) ? person
                : throw new InvalidDataException( $"{SampleDataSource.PersonalLinksConfigurationPath}: {field} {guid} is not a Cast person in this dataset." );
        }

        /// <summary>
        /// A person with sections of their own, and when they finished the last one.
        /// </summary>
        private sealed class LinkOwner
        {
            public LoadedPerson Person { get; set; }

            public List<(int Id, Guid Guid)> Sections { get; } = new List<(int Id, Guid Guid)>();

            public DateTime Arranged { get; set; }
        }

        /// <summary>
        /// When a section was made: its days before install day, in office hours.
        /// </summary>
        private DateTime Created( Guid sectionGuid, int daysAgo )
        {
            var random = new SeededRandom( $"{_seed}:personal-links:{sectionGuid}" );
            return _dates.Anchor.AddDays( -daysAgo ).AddMinutes( random.Between( 9 * 60, ( 16 * 60 ) - 1 ) );
        }
    }
}
