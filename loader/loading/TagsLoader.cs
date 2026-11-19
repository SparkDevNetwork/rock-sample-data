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
    /// Writes the church's tags on people from configuration/tags.json (decision 302): its organizational tags in their
    /// categories, staff members' personal tags, and who carries each one, as Rock's tag bar saves them.
    /// </summary>
    internal sealed class TagsLoader
    {
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );

        /// <summary>
        /// The color Rock gives a new tag.
        /// </summary>
        private const string RockTagColor = "#F4F5F7";

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public TagsLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        /// <summary>
        /// Writes every tag whose maker this dataset loads, and the people it holds. Returns a line for the log.
        /// </summary>
        public string Load( PeopleLoader people, HashSet<string> castHouseholdKeys, string mainCampusKey )
        {
            var tags = _source.Tags?.Tags;
            if ( tags == null )
            {
                return "Skipped (this package has no configuration/tags.json)";
            }

            var personEntityTypeId = _db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            var personGuids = _db.Query( $"SELECT [p].[Id], [p].[Guid] FROM [Person] AS [p] WHERE {PeopleLoader.DatasetPersonFilter( "p" )}", P( "@ForeignKey", _foreignKey ) )
                .ToDictionary( r => ( int ) r["Id"], r => ( Guid ) r["Guid"] );

            // A person is tagged only after their family first came, the day the church first knew them.
            var firstCame = people.Households.SelectMany( h => h.Members.Select( m => (m.PersonId, Day: _dates.Resolve( h.Cast.Attendance?.Started ) ?? DateTime.MinValue) ) )
                .GroupBy( m => m.PersonId ).ToDictionary( g => g.Key, g => g.Min( m => m.Day ) );
            var staff = ( _source.Staff?.Staff ?? new List<StaffMember>() )
                .Where( s => people.People.ContainsKey( s.Person ) )
                .ToDictionary( s => people.People[s.Person], s => _dates.Resolve( s.StartDate ) ?? _dates.Anchor.AddYears( -1 ) );
            var generated = people.Households.Where( h => !castHouseholdKeys.Contains( h.Cast.Key ) && h.Cast.Kind != "business" ).ToList();
            var evaluator = new DataViewEvaluator( _db, _dates.Anchor, _source.DataViews(), f => f.Steps != null ? ReportingLoader.StepsSelection( _source.Steps, f.Steps ) : null );

            var items = TableFactory.Create( "IsSystem:bool", "TagId:int", "EntityTypeId:int", "EntityGuid:guid", "Quantity:int", "Guid:guid",
                "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "ForeignKey:string" );
            var written = new List<string>();
            foreach ( var tag in tags )
            {
                var maker = tag.Maker == null ? null : people.People.GetValueOrDefault( tag.Maker.Person );
                var tagger = people.People.GetValueOrDefault( ( tag.TaggedBy ?? tag.Maker )?.Person ?? Guid.Empty );
                if ( tagger == null || ( tag.Maker != null && maker == null ) )
                {
                    continue;
                }

                // Rock's own Staff tag came with Rock, so only each staff member's start date bounds when they were tagged.
                var made = tag.RockShips ? DateTime.MinValue
                    : _dates.Anchor.Date.AddDays( -tag.CreatedDaysAgo ).AddHours( 10 ).AddMinutes( new SeededRandom( $"{_seed}:tags:{tag.Key}" ).Between( 0, 240 ) );
                var tagId = tag.RockShips ? _db.RequireIdByGuid( "Tag", tag.Guid ) : WriteTag( tag, personEntityTypeId, maker, made );

                // Each person with the first day they could be tagged: a staff member's start date, or else the day the tag was made.
                var members = new List<(LoadedPerson Person, DateTime Earliest)>();
                if ( tag.Staff )
                {
                    var taggerStart = staff.GetValueOrDefault( tagger, DateTime.MinValue );
                    members.AddRange( staff.Select( s => (s.Key, Max( s.Value, taggerStart )) ) );
                }

                members.AddRange( tag.Cast.Where( c => people.People.ContainsKey( c.Person ) ).Select( c => (people.People[c.Person], made) ) );
                if ( tag.Draw != null )
                {
                    var campus = tag.Draw.Campus == "main" ? mainCampusKey : tag.Draw.Campus == "owner" ? people.Households.First( h => h.Members.Contains( maker ) ).CampusKey : null;
                    members.AddRange( Draw( tag, campus, generated, evaluator ).Select( p => (p, made) ) );
                }

                var window = tag.TaggedWithinDays.HasValue ? _dates.Anchor.Date.AddDays( -tag.TaggedWithinDays.Value ) : DateTime.MinValue;
                var last = _dates.Anchor.Date.AddDays( -1 );
                foreach ( var (person, earliest) in members.GroupBy( m => m.Person.PersonId ).Select( g => g.First() ) )
                {
                    var random = new SeededRandom( $"{_seed}:tags:{tag.Key}:{person.Cast.Guid}" );
                    var earliestTag = Max( Max( earliest, made ), Max( firstCame[person.PersonId], window ) );
                    var first = Min( earliestTag.Date, last );

                    // A staff member is tagged on their first day, or on the tagger's first day when they started earlier.
                    var tagged = first.AddDays( tag.Staff ? 0 : random.Between( 0, ( int ) ( last - first ).TotalDays ) ).AddHours( random.Between( 9, 16 ) ).AddMinutes( random.Between( 0, 59 ) );
                    if ( tagged <= earliestTag )
                    {
                        tagged = earliestTag.AddMinutes( random.Between( 5, 60 ) );
                    }

                    items.Rows.Add( false, tagId, personEntityTypeId, personGuids[person.PersonId], 0, random.NextGuid(), tagged, tagged, tagger.AliasId, tagger.AliasId, _foreignKey );
                }

                written.Add( $"{tag.Name}{( tag.Owner == null ? string.Empty : $" ({tag.Owner.Name})" )} {members.Select( m => m.Person.PersonId ).Distinct().Count():N0}" );
            }

            _db.BulkInsert( "TaggedItem", items, keepIdentity: false );
            return $"Tags: {string.Join( ", ", written )}";
        }

        /// <summary>
        /// Writes an organizational or personal tag as Rock saves one: for people, with no qualifier, active, made by its maker.
        /// </summary>
        private int WriteTag( TagDefinition tag, int personEntityTypeId, LoadedPerson maker, DateTime made )
        {
            int? categoryId = null;
            if ( tag.Category != null )
            {
                var category = _source.Categories?.Categories.FirstOrDefault( c => c.Key == tag.Category )
                    ?? throw new InvalidDataException( $"{SampleDataSource.TagsConfigurationPath}: tag {tag.Key} is in category {tag.Category}, which configuration/categories.json does not have." );
                categoryId = _db.RequireIdByGuid( "Category", category.Guid );
            }

            return ( int ) _db.Scalar( @"
INSERT INTO [Tag] ( [IsSystem], [EntityTypeId], [EntityTypeQualifierColumn], [EntityTypeQualifierValue], [Name], [Description], [Order], [OwnerPersonAliasId],
    [IsActive], [CategoryId], [IconCssClass], [BackgroundColor], [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @EntityTypeId, '', '', @Name, @Description, 0, @OwnerAliasId, 1, @CategoryId, @Icon, @Color, @Guid, @Made, @Made, @MakerAliasId, @MakerAliasId, @ForeignKey )",
                P( "@EntityTypeId", personEntityTypeId ), P( "@Name", tag.Name ), P( "@Description", tag.Description ), P( "@OwnerAliasId", tag.Owner == null ? null : maker.AliasId ),
                P( "@CategoryId", categoryId ), P( "@Icon", tag.IconCssClass ), P( "@Color", tag.BackgroundColor ?? RockTagColor ), P( "@Guid", tag.Guid ),
                P( "@Made", made ), P( "@MakerAliasId", maker.AliasId ), P( "@ForeignKey", _foreignKey ) );
        }

        /// <summary>
        /// The generated people a tag's draw adds: each match by the draw's chance, or the draw's count of matches. Each
        /// person's draw comes from their own seeded stream, so the same people are tagged however many others match.
        /// </summary>
        private List<LoadedPerson> Draw( TagDefinition tag, string campus, List<LoadedHousehold> generated, DataViewEvaluator evaluator )
        {
            var rule = tag.Draw;
            HashSet<int> inView = null;
            if ( rule.DataView != null )
            {
                var view = _source.DataViews().GetValueOrDefault( rule.DataView )
                    ?? throw new InvalidDataException( $"{SampleDataSource.TagsConfigurationPath}: tag {tag.Key} draws from data view {rule.DataView}, which is not a data view in any configuration file." );
                inView = _db.IdByGuid( "DataView", view.Guid ) == null ? new HashSet<int>() : evaluator.EvaluateView( view ).Keys.ToHashSet();
            }

            var firstCame = rule.FirstCameWithinDays.HasValue ? _dates.Anchor.Date.AddDays( -rule.FirstCameWithinDays.Value ) : ( DateTime? ) null;
            var matches = generated
                .Where( h => campus == null || h.CampusKey == campus )
                .Where( h => !rule.LivesAlone || h.Members.Count == 1 )
                .Where( h => firstCame == null || _dates.Resolve( h.Cast.Attendance?.Started ) >= firstCame )
                .SelectMany( h => h.Members )
                .Where( p => p.CanAttend && p.BirthDate.HasValue && _dates.AgeOn( p.BirthDate.Value ) >= rule.MinAge && ( rule.MaxAge == null || _dates.AgeOn( p.BirthDate.Value ) <= rule.MaxAge ) )
                .Where( p => rule.Statuses.Count == 0 || rule.Statuses.Contains( p.Cast.ConnectionStatus, StringComparer.OrdinalIgnoreCase ) )
                .Where( p => inView == null || inView.Contains( p.PersonId ) );

            return rule.Percent.HasValue
                ? matches.Where( p => new SeededRandom( $"{_seed}:tags:{tag.Key}:draw:{p.Cast.Guid}" ).Chance( rule.Percent.Value / 100 ) ).ToList()
                : matches.OrderBy( p => SeededRandom.StableGuid( $"{_seed}:tags:{tag.Key}:draw:{p.Cast.Guid}" ) ).Take( rule.Count ?? 0 ).ToList();
        }

        private static DateTime Max( DateTime a, DateTime b ) => a > b ? a : b;

        private static DateTime Min( DateTime a, DateTime b ) => a < b ? a : b;
    }
}
