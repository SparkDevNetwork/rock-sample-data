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
    /// Writes the church's person signal types from configuration/signals.json in the church setup, and in the Signals step
    /// the Cast's signals, the Crowd's Care Team Follow-ups, and each person's top signal (issue 45).
    /// </summary>
    internal sealed class SignalsLoader
    {
        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;

        public SignalsLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string foreignKey, string seed )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
        }

        public bool HasConfiguration => _source.Signals != null;

        /// <summary>
        /// Writes each signal type with its security, as Rock's signal type editor and security editor save them. Returns how many.
        /// </summary>
        public int LoadTypes()
        {
            var now = DateTime.Now;
            var security = new SecurityRuleWriter( _db, _source.SecurityRoles, _foreignKey );
            foreach ( var type in _source.Signals.Types )
            {
                // Rock's signal type editor gives a new type the next order after the last one, or 0 for the first
                // (SignalTypeDetail.TryGetEntityForEditAction).
                var order = _db.Scalar( "SELECT MAX( [Order] ) FROM [SignalType]" ) is int last ? last + 1 : 0;
                var id = ( int ) _db.Scalar( @"
INSERT INTO [SignalType] ( [Name], [Description], [SignalColor], [SignalIconCssClass], [Order], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
OUTPUT INSERTED.[Id]
VALUES ( @Name, @Description, @Color, @Icon, @Order, @Guid, @ForeignKey, @Now, @Now )",
                    P( "@Name", type.Name ), P( "@Description", type.Description ), P( "@Color", type.Color ), P( "@Icon", type.Icon ),
                    P( "@Order", order ), P( "@Guid", type.Guid ), P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );
                security.Write( "Rock.Model.SignalType", id, type.Security, now, $"{SampleDataSource.SignalsConfigurationPath}: signal type '{type.Key}'" );
            }

            return _source.Signals.Types.Count;
        }

        /// <summary>
        /// Writes the Cast's signals and each campus's Care Team Follow-ups, then the top signal of everyone who has one.
        /// </summary>
        /// <param name="campusPastors">Each loaded campus's campus pastor, who owns its Crowd signals.</param>
        public string Load( PeopleLoader people, ISet<string> castHouseholdKeys, IReadOnlyDictionary<string, LoadedPerson> campusPastors )
        {
            var types = _source.Signals.Types.ToDictionary( t => t.Key, t => ReadType( t ) );
            var planned = new List<PlannedSignal>();
            var castSkipped = 0;
            foreach ( var signal in _source.Signals.Cast )
            {
                var plan = PlanCast( signal, people, types );
                if ( plan == null )
                {
                    castSkipped++;
                    continue;
                }

                planned.Add( plan );
            }

            var castCount = planned.Count;
            if ( _source.Signals.Crowd != null )
            {
                var generated = people.Households.Where( h => !castHouseholdKeys.Contains( h.Cast.Key ) && h.Cast.Kind != "business" ).ToList();
                foreach ( var campus in generated.Select( h => h.CampusKey ).Distinct().OrderBy( k => k, StringComparer.Ordinal ) )
                {
                    planned.AddRange( PlanCampus( campus, generated.Where( h => h.CampusKey == campus ).ToList(), campusPastors[campus], types[_source.Signals.Crowd.Type] ) );
                }
            }

            var now = DateTime.Now;
            foreach ( var signal in planned )
            {
                signal.Id = ( int ) _db.Scalar( @"
INSERT INTO [PersonSignal] ( [PersonId], [SignalTypeId], [OwnerPersonAliasId], [Note], [ExpirationDate], [Guid], [ForeignKey],
    [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId] )
OUTPUT INSERTED.[Id]
VALUES ( @PersonId, @TypeId, @OwnerAliasId, @Note, @ExpirationDate, @Guid, @ForeignKey, @Added, @Added, @AddedByAliasId, @AddedByAliasId )",
                    P( "@PersonId", signal.PersonId ), P( "@TypeId", signal.Type.Id ), P( "@OwnerAliasId", signal.OwnerAliasId ), P( "@Note", signal.Note ),
                    P( "@ExpirationDate", signal.Expires ), P( "@Guid", signal.Guid ), P( "@ForeignKey", _foreignKey ), P( "@Added", signal.Added ),
                    P( "@AddedByAliasId", signal.AddedByAliasId ) );
            }

            var flagged = WriteTopSignals( planned, _dates.Anchor.Date.Add( now.TimeOfDay ) );
            var skippedText = castSkipped == 0 ? string.Empty : $", {castSkipped} Cast signals skipped because their people are on a campus this dataset leaves out";
            return $"{castCount} Cast and {planned.Count - castCount} Crowd signals, {flagged} people flagged{skippedText}";
        }

        /// <summary>
        /// The type's Id and order, which the church setup wrote.
        /// </summary>
        private LoadedSignalType ReadType( SignalTypeDefinition type )
        {
            var row = _db.Query( "SELECT [Id], [Order] FROM [SignalType] WHERE [Guid] = @Guid", P( "@Guid", type.Guid ) ).FirstOrDefault()
                ?? throw new InvalidDataException( $"{SampleDataSource.SignalsConfigurationPath}: signal type '{type.Key}' was not written by the church setup." );
            return new LoadedSignalType { Definition = type, Id = ( int ) row["Id"], Order = ( int ) row["Order"] };
        }

        /// <summary>
        /// A Cast signal, or null when its person or a staff member it names is not in this dataset.
        /// </summary>
        private PlannedSignal PlanCast( CastSignal signal, PeopleLoader people, Dictionary<string, LoadedSignalType> types )
        {
            if ( signal.EndsInDays.HasValue && signal.EndedDaysAgo.HasValue )
            {
                throw new InvalidDataException( $"{SampleDataSource.SignalsConfigurationPath}: {signal.Name}'s signal both ends and has ended. Give one of endsInDays or endedDaysAgo." );
            }

            if ( signal.EndedDaysAgo.HasValue && signal.EndedDaysAgo.Value >= signal.AddedDaysAgo )
            {
                throw new InvalidDataException( $"{SampleDataSource.SignalsConfigurationPath}: {signal.Name}'s signal ended before it was added." );
            }

            if ( !types.TryGetValue( signal.Type, out var type ) )
            {
                throw new InvalidDataException( $"{SampleDataSource.SignalsConfigurationPath}: {signal.Name}'s signal has type '{signal.Type}', which is not one of its types." );
            }

            if ( !people.People.TryGetValue( signal.Person, out var person ) || !people.People.TryGetValue( signal.Owner, out var owner ) )
            {
                return null;
            }

            var addedBy = owner;
            if ( signal.AddedBy.HasValue && !people.People.TryGetValue( signal.AddedBy.Value, out addedBy ) )
            {
                return null;
            }

            var random = new SeededRandom( $"{_seed}:signal:{signal.Guid}" );
            var expires = signal.EndsInDays.HasValue ? _dates.Anchor.AddDays( signal.EndsInDays.Value )
                : signal.EndedDaysAgo.HasValue ? _dates.Anchor.AddDays( -signal.EndedDaysAgo.Value )
                : ( DateTime? ) null;
            return new PlannedSignal
            {
                Guid = signal.Guid,
                PersonId = person.PersonId,
                Type = type,
                OwnerAliasId = owner.AliasId,
                AddedByAliasId = addedBy.AliasId,
                Note = signal.Note,
                Added = OfficeHours( _dates.Anchor.AddDays( -signal.AddedDaysAgo ), random ),
                Expires = expires
            };
        }

        /*
            11/17/26 - CLAUDE

            A campus pastor follows up with a few people at a time. Each campus with generated households gets the same
            number of Care Team Follow-ups, on adults their pastor knows (the connection statuses in the file), at most
            one per household. A few are still open, and the rest ended at some point in the last year. The campus's own
            stream picks them, so no other row changes.

            Reason: Issue 45 asks for a few Care Team Follow-ups per campus, owned by campus pastors, most of them ended.
        */
        private List<PlannedSignal> PlanCampus( string campusKey, List<LoadedHousehold> households, LoadedPerson pastor, LoadedSignalType type )
        {
            var crowd = _source.Signals.Crowd;
            var random = new SeededRandom( $"{_seed}:signals:{campusKey}" );
            var candidates = households
                .Select( h => h.Members.Where( m => m.IsAdult && m.CanAttend && crowd.ConnectionStatuses.Contains( m.Cast.ConnectionStatus ) ).ToList() )
                .Where( adults => adults.Count > 0 )
                .ToList();
            var notes = crowd.Notes.ToList();
            var planned = new List<PlannedSignal>();
            for ( var i = 0; i < crowd.PerCampus && candidates.Count > 0; i++ )
            {
                var index = random.Between( 0, candidates.Count - 1 );
                var person = random.Pick( candidates[index] );
                candidates.RemoveAt( index );
                var note = notes[random.Between( 0, notes.Count - 1 )];
                notes.Remove( note );
                if ( notes.Count == 0 )
                {
                    notes = crowd.Notes.ToList();
                }

                DateTime added;
                DateTime? expires;
                if ( i < crowd.OpenWithoutEnd + crowd.OpenWithEnd )
                {
                    added = _dates.Anchor.AddDays( -Between( random, crowd.OpenAddedDaysAgo ) );
                    expires = i < crowd.OpenWithoutEnd ? null : _dates.Anchor.AddDays( Between( random, crowd.EndsInDays ) );
                }
                else
                {
                    var ended = _dates.Anchor.AddDays( -Between( random, crowd.EndedDaysAgo ) );
                    added = ended.AddDays( -Between( random, crowd.AddedDaysBeforeEnd ) );
                    expires = ended;
                }

                planned.Add( new PlannedSignal
                {
                    Guid = random.NextGuid(),
                    PersonId = person.PersonId,
                    Type = type,
                    OwnerAliasId = pastor.AliasId,
                    AddedByAliasId = pastor.AliasId,
                    Note = note,
                    Added = OfficeHours( added, random ),
                    Expires = expires
                } );
            }

            return planned;
        }

        /*
            11/17/26 - CLAUDE

            Rock keeps each person's top signal on the person: TopSignalId, TopSignalColor, and TopSignalIconCssClass.
            Person.CalculateSignals sets them from the person's signals that have no end date or end at or after now,
            ordered by type order, then type Id, and clears them when none is open. Rock runs it when a signal is saved or
            deleted, and its Calculate Person Signals job runs it every night at 3:15 AM, which clears a signal the night
            after it ends. The Top Person Signal badge shows the flag only while the person has an open signal.

            Reason: The badge shows each person's right flag on install day, and an ended signal shows none.
        */
        private int WriteTopSignals( List<PlannedSignal> planned, DateTime now )
        {
            var flagged = 0;
            foreach ( var person in planned.GroupBy( s => s.PersonId ) )
            {
                var top = person
                    .Where( s => !s.Expires.HasValue || s.Expires.Value >= now )
                    .OrderBy( s => s.Type.Order )
                    .ThenBy( s => s.Type.Id )
                    .ThenBy( s => s.Id )
                    .FirstOrDefault();
                if ( top == null )
                {
                    continue;
                }

                _db.Execute( "UPDATE [Person] SET [TopSignalId] = @SignalId, [TopSignalColor] = @Color, [TopSignalIconCssClass] = @Icon WHERE [Id] = @Id",
                    P( "@SignalId", top.Id ), P( "@Color", top.Type.Definition.Color ), P( "@Icon", top.Type.Definition.Icon ), P( "@Id", person.Key ) );
                flagged++;
            }

            return flagged;
        }

        private static int Between( SeededRandom random, DayRange range ) => random.Between( range.Min, range.Max );

        /// <summary>
        /// A time in office hours on the day.
        /// </summary>
        private static DateTime OfficeHours( DateTime day, SeededRandom random ) => day.Date.AddMinutes( random.Between( 9 * 60, ( 17 * 60 ) - 1 ) );

        private sealed class LoadedSignalType
        {
            public SignalTypeDefinition Definition { get; set; }

            public int Id { get; set; }

            public int Order { get; set; }
        }

        private sealed class PlannedSignal
        {
            public Guid Guid { get; set; }

            public int Id { get; set; }

            public int PersonId { get; set; }

            public LoadedSignalType Type { get; set; }

            public int OwnerAliasId { get; set; }

            public int AddedByAliasId { get; set; }

            public string Note { get; set; }

            public DateTime Added { get; set; }

            /// <summary>
            /// When the signal ends, at midnight, as Rock's date picker saves it, or null.
            /// </summary>
            public DateTime? Expires { get; set; }
        }
    }
}
