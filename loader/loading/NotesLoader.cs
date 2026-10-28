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

using System.Globalization;
using System.Text;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Sets up the note types on person records and loads notes: the Cast's hand-written notes, and
    /// generated notes for generated households (decision 78).
    /// </summary>
    internal sealed class NotesLoader
    {
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );
        private static readonly Guid NoteTypeEntityTypeGuid = new Guid( "337EED57-D4AB-4EED-BBDB-0CB3A467DBCC" );

        /// <summary>
        /// The caption Rock's Note.UpdateCaption gives a private note.
        /// </summary>
        private const string PrivateCaption = "You - Personal Note";

        /// <summary>
        /// Rock's NoteApprovalStatus.Approved, which Rock sets on every note whose type needs no approval.
        /// </summary>
        private const int ApprovedStatus = 1;

        /// <summary>
        /// Rock's NoteFormatType.Structured: plain text with mentions, as Rock's note editor writes it.
        /// </summary>
        private const int StructuredFormat = 2;

        /// <summary>
        /// The window the profile's volumes describe.
        /// </summary>
        private const int RecentDays = 730;

        /// <summary>
        /// The most details a generated note adds after its opener. Four reach the profile's longest notes.
        /// </summary>
        private const int MaxDetails = 4;

        private static readonly HashSet<string> GuestStatuses = new HashSet<string> { "prospect", "participant", "known" };
        private static readonly HashSet<string> AttenderStatuses = new HashSet<string> { "connected", "engaged", "leading" };

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly ProfileTargets _profile;
        private readonly AnchorDates _dates;
        private readonly string _foreignKey;
        private readonly string _seed;
        private readonly int _historyYears;
        private readonly RockKeys _keys;

        private readonly List<NoteRow> _rows = new List<NoteRow>();
        private Dictionary<string, int> _typeIds;
        private Dictionary<string, NoteTypeDefinition> _types;
        private Dictionary<string, LoadedPerson> _staff;

        public NotesLoader( RockDatabase db, SampleDataSource source, ProfileTargets profile, AnchorDates dates, string foreignKey, string seed, int historyYears, RockKeys keys )
        {
            _db = db;
            _source = source;
            _profile = profile;
            _dates = dates;
            _foreignKey = foreignKey;
            _seed = seed;
            _historyYears = historyYears;
            _keys = keys;
        }

        /// <summary>
        /// Sets up the note types and writes every note.
        /// </summary>
        public void Load( PeopleLoader people, HashSet<string> castHouseholdKeys, string mainCampusKey, SecurityRolesLoader securityRoles )
        {
            var configuration = _source.Notes;
            if ( configuration == null )
            {
                _db.Log.Write( "  Skipped (this package has no configuration/notes.json)" );
                return;
            }

            _types = configuration.Types.ToDictionary( t => t.Key );
            _typeIds = ConfigureNoteTypes( configuration );
            _staff = configuration.Staff.ToDictionary( s => s.Key, s => people.People.TryGetValue( s.Guid, out var person )
                ? person
                : throw new InvalidOperationException( $"configuration/notes.json lists staff member {s.Key} ({s.Guid}), who is not a loaded person." ) );

            var castCount = AddCastNotes( people, castHouseholdKeys );
            var generatedCount = AddGeneratedNotes( people, castHouseholdKeys, mainCampusKey, configuration, securityRoles );
            Insert();

            var byType = string.Join( ", ", _types.Values.OrderBy( t => t.Order ).Select( t => $"{t.Name} {_rows.Count( r => r.TypeKey == t.Key && r.ParentIndex == null ):N0}" ) );
            _db.Log.Write( $"  Notes: {castCount:N0} Cast and {generatedCount:N0} generated, plus {_rows.Count( r => r.ParentIndex != null ):N0} replies ({byType})" );
            _db.Log.Write( _keys?.IdKey( 1 ) != null
                ? "  Mentions link to staff (keyed from Rock's DataEncryptionKey)"
                : "  No Rock DataEncryptionKey (give a Rock web.config); mentions are plain names" );
        }

        #region Note types

        /*
            10/20/26 - CLAUDE

            Rock's own Personal, Pastoral, and Phone note types are kept and updated in place, because
            Rock code finds them by Guid. Pastoral and Phone ship hidden from the "add a note" picker, so
            they are made selectable. Every type gets View rules, which a fresh Rock does not have: the
            measured source locked viewing down on nearly every type, and good practice keeps care and
            counseling notes from anyone who is not staff.

            Reason: Six note types with the security a well-run church gives them (decision 78).
        */

        /// <summary>
        /// Updates Rock's note types, adds the new ones, and adds their security. Returns type Ids by key.
        /// </summary>
        private Dictionary<string, int> ConfigureNoteTypes( NotesConfiguration configuration )
        {
            var now = DateTime.Now;
            var personEntityTypeId = _db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            var noteTypeEntityTypeId = _db.RequireIdByGuid( "EntityType", NoteTypeEntityTypeGuid );
            var ids = new Dictionary<string, int>();

            foreach ( var type in configuration.Types.OrderBy( t => t.Order ) )
            {
                int id;
                if ( type.RockType )
                {
                    id = _db.RequireIdByGuid( "NoteType", type.Guid );
                    _db.Execute( @"
UPDATE [NoteType]
SET [UserSelectable] = 1, [IconCssClass] = @IconCssClass, [Order] = @Order, [AllowsReplies] = 1, [AllowsWatching] = 1, [IsMentionEnabled] = 1, [ModifiedDateTime] = @Now
WHERE [Id] = @Id",
                        P( "@IconCssClass", type.IconCssClass ), P( "@Order", type.Order ), P( "@Now", now ), P( "@Id", id ) );
                }
                else
                {
                    id = ( int ) _db.Scalar( @"
INSERT INTO [NoteType] ( [IsSystem], [EntityTypeId], [Name], [EntityTypeQualifierColumn], [EntityTypeQualifierValue], [Guid], [UserSelectable], [IconCssClass], [Order],
    [RequiresApprovals], [AllowsWatching], [AllowsReplies], [SendApprovalNotifications], [AutoWatchAuthors], [AllowsAttachments], [FormatType], [IsMentionEnabled],
    [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @EntityTypeId, @Name, '', '', @Guid, 1, @IconCssClass, @Order,
    0, 1, 1, 0, 0, 0, @FormatType, 1,
    @Now, @Now, @ForeignKey )",
                        P( "@EntityTypeId", personEntityTypeId ), P( "@Name", type.Name ), P( "@Guid", type.Guid ), P( "@IconCssClass", type.IconCssClass ),
                        P( "@Order", type.Order ), P( "@FormatType", StructuredFormat ), P( "@Now", now ), P( "@ForeignKey", _foreignKey ) );
                }

                ids[type.Key] = id;
                AddSecurity( noteTypeEntityTypeId, id, "View", type.Viewers, configuration, now );

                // Rock's own types already allow staff to edit. New types get the same rules as their viewers.
                if ( !type.RockType )
                {
                    AddSecurity( noteTypeEntityTypeId, id, "Edit", type.Editors, configuration, now );
                }
            }

            return ids;
        }

        /// <summary>
        /// Allows each role in order, then denies all users, as Rock's security editor writes them.
        /// </summary>
        private void AddSecurity( int noteTypeEntityTypeId, int noteTypeId, string action, List<string> roleKeys, NotesConfiguration configuration, DateTime now )
        {
            var order = 0;
            foreach ( var roleKey in roleKeys )
            {
                var groupId = _db.RequireIdByGuid( "Group", configuration.StaffRoles[roleKey] );
                InsertAuth( noteTypeEntityTypeId, noteTypeId, order++, action, "A", 0, groupId, now );
            }

            // SpecialRole 1 is Rock's "All Users".
            InsertAuth( noteTypeEntityTypeId, noteTypeId, order, action, "D", 1, null, now );
        }

        private void InsertAuth( int entityTypeId, int entityId, int order, string action, string allowOrDeny, int specialRole, int? groupId, DateTime now )
        {
            _db.Execute( @"
INSERT INTO [Auth] ( [EntityTypeId], [EntityId], [Order], [Action], [AllowOrDeny], [SpecialRole], [GroupId], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
VALUES ( @EntityTypeId, @EntityId, @Order, @Action, @AllowOrDeny, @SpecialRole, @GroupId, NEWID(), @Now, @Now, @ForeignKey )",
                P( "@EntityTypeId", entityTypeId ), P( "@EntityId", entityId ), P( "@Order", order ), P( "@Action", action ), P( "@AllowOrDeny", allowOrDeny ),
                P( "@SpecialRole", specialRole ), P( "@GroupId", groupId ), P( "@Now", now ), P( "@ForeignKey", _foreignKey ) );
        }

        #endregion Note types

        #region Cast notes

        /// <summary>
        /// Adds the hand-written notes in cast/notes. Returns the number of notes, not counting replies.
        /// </summary>
        private int AddCastNotes( PeopleLoader people, HashSet<string> castHouseholdKeys )
        {
            var count = 0;
            var households = people.Households.Where( h => castHouseholdKeys.Contains( h.Cast.Key ) ).ToDictionary( h => h.Cast.Key );
            foreach ( var file in _source.ReadCastNotes() )
            {
                if ( !households.TryGetValue( file.Household, out var household ) )
                {
                    throw new InvalidOperationException( $"cast/notes/{file.Household}.json is for a household that is not loaded." );
                }

                foreach ( var note in file.Notes )
                {
                    var about = household.Members.FirstOrDefault( m => m.Cast.Key == note.About )
                        ?? throw new InvalidOperationException( $"cast/notes/{file.Household}.json: note {note.Key} is about {note.About}, who is not in the household." );
                    var type = RequireType( note.Type, file.Household, note.Key );
                    var written = Written( note.Written, note.Time );
                    var text = CastText( note.Text );

                    var index = Add( new NoteRow
                    {
                        Guid = note.Guid,
                        TypeKey = type.Key,
                        PersonId = about.PersonId,
                        AuthorAliasId = RequireStaff( note.Author, file.Household, note.Key ).AliasId,
                        Written = written,
                        Text = text,
                        IsAlert = note.IsAlert || type.IsAlwaysAlert,
                        IsPrivate = note.IsPrivate,
                        IsPinned = note.IsPinned
                    } );
                    count++;

                    foreach ( var reply in note.Replies )
                    {
                        Add( new NoteRow
                        {
                            Guid = reply.Guid,
                            TypeKey = type.Key,
                            PersonId = about.PersonId,
                            AuthorAliasId = RequireStaff( reply.Author, file.Household, note.Key ).AliasId,
                            Written = Written( reply.Written, reply.Time ),
                            Text = CastText( reply.Text ),
                            ParentIndex = index
                        } );
                    }
                }
            }

            return count;
        }

        private NoteTypeDefinition RequireType( string key, string household, string noteKey )
        {
            return _types.TryGetValue( key, out var type ) ? type : throw new InvalidOperationException( $"cast/notes/{household}.json: note {noteKey} has unknown type {key}." );
        }

        private LoadedPerson RequireStaff( string key, string household, string noteKey )
        {
            return _staff.TryGetValue( key, out var person ) ? person : throw new InvalidOperationException( $"cast/notes/{household}.json: note {noteKey} has unknown author {key}." );
        }

        private DateTime Written( RelativeDate written, string time )
        {
            var date = _dates.Resolve( written ) ?? _dates.Anchor;
            return string.IsNullOrEmpty( time ) ? date : date.Date.Add( TimeSpan.Parse( time, CultureInfo.InvariantCulture ) );
        }

        /// <summary>
        /// A Cast note's text as Rock stores it: escaped, with each {@staffkey} turned into a mention.
        /// </summary>
        private string CastText( string text )
        {
            var html = Escape( text );
            foreach ( var (key, person) in _staff )
            {
                html = html.Replace( $"{{@{key}}}", Mention( person ) );
            }

            return html;
        }

        #endregion Cast notes

        #region Generated notes

        /*
            10/20/26 - CLAUDE

            Generated notes go only on generated households. Cast notes are all hand-written, so the
            Cast's notes are the same on every load and tests can rely on them. Volumes and feature use
            come from personNotes.design in the profile, which is set above the measured rates on
            purpose: the sample church shows what good note use looks like, not what most churches do.

            Reason: Stable Cast notes for tests, and a full notes timeline for demos (decision 78).
        */

        /// <summary>
        /// Adds generated notes for every generated person. Returns the number of notes, not counting replies.
        /// </summary>
        private int AddGeneratedNotes( PeopleLoader people, HashSet<string> castHouseholdKeys, string mainCampusKey, NotesConfiguration configuration, SecurityRolesLoader securityRoles )
        {
            var generated = people.Households.Where( h => !castHouseholdKeys.Contains( h.Cast.Key ) && h.Cast.Kind != "business" ).ToList();
            if ( generated.Count == 0 )
            {
                return 0;
            }

            var design = new Design( _profile );
            var pools = _types.Keys.ToDictionary( k => k, k => _source.ReadNotePool( k ) );
            var shared = _source.ReadSharedNotePool();
            var campusNames = _source.Campuses.Campuses.ToDictionary( c => c.Key, c => c.Name );
            var campusPastors = CampusPastors.Pick( people, generated, mainCampusKey, _source.Staff );

            // Campus pastors join the staff roles that let them view what they write, Counseling included.
            foreach ( var pastor in campusPastors.Values )
            {
                foreach ( var roleKey in configuration.CampusPastor.Roles )
                {
                    securityRoles.Add( pastor.PersonId, configuration.StaffRoles[roleKey] );
                }
            }

            if ( campusPastors.Count > 0 )
            {
                _db.Log.Write( $"  Campus pastors: {string.Join( ", ", campusPastors.OrderBy( p => p.Key ).Select( p => $"{p.Key} {p.Value.Cast.NickName ?? p.Value.Cast.FirstName} {p.Value.Cast.LastName}" ) )}" );
            }
            var count = 0;

            foreach ( var household in generated )
            {
                foreach ( var person in household.Members.Where( m => m.CanAttend && m.Cast.RecordType != "business" ) )
                {
                    var random = new SeededRandom( $"{_seed}:notes:{person.Cast.Guid}" );
                    var audience = Audience( person );
                    if ( audience == null || !random.Chance( design.PctNoted( audience == "child" ? "child" : person.Cast.ConnectionStatus.ToLowerInvariant() ) / 100 ) )
                    {
                        continue;
                    }

                    var recentCount = NotesPerPerson( random, design.NotesPerPersonMix );
                    var olderExpected = recentCount * design.OlderNotesPerYearRatio * Math.Max( 0, _historyYears - 2 ) / 2.0;
                    var olderCount = ( int ) olderExpected + ( random.Chance( olderExpected - Math.Floor( olderExpected ) ) ? 1 : 0 );

                    var context = new NoteContext
                    {
                        Person = person,
                        Audience = audience,
                        CampusName = campusNames.GetValueOrDefault( household.CampusKey, string.Empty ),
                        CampusPastor = household.CampusKey == mainCampusKey ? null : campusPastors.GetValueOrDefault( household.CampusKey ),
                        IsMainCampus = household.CampusKey == mainCampusKey
                    };

                    var personRows = new List<int>();
                    for ( var n = 0; n < recentCount + olderCount; n++ )
                    {
                        var isRecent = n < recentCount;
                        var written = isRecent
                            ? PickTime( random, design, _dates.Anchor.AddDays( -RecentDays ), _dates.Anchor.AddDays( -1 ) )
                            : PickTime( random, design, _dates.Anchor.AddDays( -365 * _historyYears ), _dates.Anchor.AddDays( -RecentDays - 1 ) );
                        personRows.Add( AddGeneratedNote( random, design, context, configuration, pools, shared, written ) );
                        count++;
                    }

                    // One pinned note on a few people: the newest note everyone can see.
                    if ( random.Chance( design.PctNotedPeopleWithPinnedNote / 100 ) )
                    {
                        var pinned = personRows.Select( i => _rows[i] ).Where( r => !r.IsPrivate ).OrderByDescending( r => r.Written ).FirstOrDefault();
                        if ( pinned != null )
                        {
                            pinned.IsPinned = true;
                        }
                    }

                    foreach ( var index in personRows )
                    {
                        AddReplies( random, design, context, shared, index );
                    }
                }
            }

            return count;
        }

        /// <summary>
        /// Adds one generated note and returns its index.
        /// </summary>
        private int AddGeneratedNote( SeededRandom random, Design design, NoteContext context, NotesConfiguration configuration, Dictionary<string, Dictionary<string, NotePoolSection>> pools, SharedNotePool shared, DateTime written )
        {
            var weights = _types.Values.Where( t => t.Weights.ContainsKey( context.Audience ) ).ToDictionary( t => t.Key, t => t.Weights[context.Audience] );
            var type = _types[random.Pick( weights )];
            var section = pools[type.Key][context.Audience];
            var author = PickAuthor( random, type, context, configuration );

            var isAlert = type.IsAlwaysAlert;
            string text;
            if ( !isAlert && section.Alerts.Count > 0 && random.Chance( design.PctPersonalNotesThatAreAlerts / 100 ) )
            {
                isAlert = true;
                text = Escape( Fill( random.Pick( section.Alerts ), context ) );
            }
            else
            {
                text = Escape( Compose( random, design, section, context ) );
                var mentionable = _staff.Values.Where( s => s.AliasId != author.AliasId && s.FamilyId != context.Person.FamilyId ).ToList();
                if ( !type.IsConfidential && shared.MentionLines.Count > 0 && mentionable.Count > 0 && random.Chance( design.PctNotesWithMention / 100 ) )
                {
                    var line = Escape( Fill( random.Pick( shared.MentionLines ), context ) );
                    text = $"{text} {line.Replace( "{staff}", Mention( random.Pick( mentionable ) ) )}";
                }
            }

            return Add( new NoteRow
            {
                Guid = random.NextGuid(),
                TypeKey = type.Key,
                PersonId = context.Person.PersonId,
                AuthorAliasId = author.AliasId,
                Written = written,
                Text = text,
                IsAlert = isAlert,
                IsPrivate = !isAlert && random.Chance( design.PctPrivate( type.Key ) / 100 )
            } );
        }

        /// <summary>
        /// Adds a reply to a few notes, and sometimes the author's answer. The reply comes from another of the
        /// type's authors, or from any other staff member when the type has no other author and is not confidential.
        /// </summary>
        private void AddReplies( SeededRandom random, Design design, NoteContext context, SharedNotePool shared, int index )
        {
            var note = _rows[index];
            if ( note.IsPrivate || shared.Replies.Count == 0 || !random.Chance( design.PctTopLevelNotesWithReply / 100 ) )
            {
                return;
            }

            var type = _types[note.TypeKey];
            bool CanReply( LoadedPerson staff ) => staff.AliasId != note.AuthorAliasId && staff.FamilyId != context.Person.FamilyId;
            var others = type.Authors.Keys.Select( k => _staff[k] ).Where( CanReply ).ToList();
            if ( others.Count == 0 && !type.IsConfidential )
            {
                others = _staff.Values.Where( CanReply ).ToList();
            }

            if ( others.Count == 0 )
            {
                return;
            }

            var replyAuthor = random.Pick( others );
            var first = ReplyTime( random, note.Written );
            if ( first == null )
            {
                return;
            }

            var replyIndex = Add( new NoteRow
            {
                Guid = random.NextGuid(),
                TypeKey = note.TypeKey,
                PersonId = note.PersonId,
                AuthorAliasId = replyAuthor.AliasId,
                Written = first.Value,
                Text = Escape( Fill( random.Pick( shared.Replies ), context ) ),
                ParentIndex = index
            } );

            var second = random.Chance( design.PctRepliesWithSecondReply / 100 ) ? ReplyTime( random, first.Value ) : null;
            if ( second != null )
            {
                Add( new NoteRow
                {
                    Guid = random.NextGuid(),
                    TypeKey = note.TypeKey,
                    PersonId = note.PersonId,
                    AuthorAliasId = note.AuthorAliasId,
                    Written = second.Value,
                    Text = Escape( Fill( random.Pick( shared.Replies ), context ) ),
                    ParentIndex = replyIndex
                } );
            }
        }

        /// <summary>
        /// A reply time on the same day or up to three days later, during the work day. Null when that
        /// would be after install day.
        /// </summary>
        private DateTime? ReplyTime( SeededRandom random, DateTime after )
        {
            var day = after.Date.AddDays( random.Between( 0, 3 ) );
            var time = day.AddHours( random.Between( 8, 17 ) ).AddMinutes( random.Between( 0, 59 ) );
            if ( time <= after )
            {
                time = after.AddMinutes( random.Between( 20, 180 ) );
            }

            return time < _dates.Anchor ? time : null;
        }

        /// <summary>
        /// The note's audience: child under 18, guest or attender by connection status, or null for
        /// a status the design does not cover.
        /// </summary>
        private string Audience( LoadedPerson person )
        {
            if ( person.BirthDate.HasValue && _dates.AgeOn( person.BirthDate.Value ) < 18 )
            {
                return "child";
            }

            var status = person.Cast.ConnectionStatus?.ToLowerInvariant();
            return status != null && GuestStatuses.Contains( status ) ? "guest"
                : status != null && AttenderStatuses.Contains( status ) ? "attender"
                : null;
        }

        /// <summary>
        /// How many notes a noted person gets in the recent window, from the design's mix.
        /// </summary>
        private static int NotesPerPerson( SeededRandom random, IReadOnlyDictionary<string, double> mix )
        {
            return random.Pick( mix ) switch
            {
                "1" => 1,
                "2-3" => random.Between( 2, 3 ),
                "4-9" => random.Between( 4, 9 ),
                _ => random.Between( 10, 16 )
            };
        }

        /// <summary>
        /// A time between two days, with the weekday and hour band from the profile's measured shape.
        /// </summary>
        private static DateTime PickTime( SeededRandom random, Design design, DateTime first, DateTime last )
        {
            var day = first.Date.AddDays( random.Between( 0, Math.Max( 0, ( int ) ( last.Date - first.Date ).TotalDays ) ) );

            // Move to the picked weekday in the same Monday-to-Sunday week, then back inside the range.
            var weekday = Array.IndexOf( Design.Weekdays, random.Pick( design.WeekdayMix ) );
            day = day.AddDays( weekday - ( ( ( int ) day.DayOfWeek + 6 ) % 7 ) );
            while ( day > last.Date )
            {
                day = day.AddDays( -7 );
            }

            while ( day < first.Date )
            {
                day = day.AddDays( 7 );
            }

            var band = random.Pick( design.HourBandMix ).Split( '-' );
            var hour = random.Between( int.Parse( band[0], CultureInfo.InvariantCulture ), int.Parse( band[1], CultureInfo.InvariantCulture ) );
            return day.AddHours( hour ).AddMinutes( random.Between( 0, 59 ) );
        }

        /// <summary>
        /// The note's author. On the main campus, the type's weighted staff. On other campuses, the
        /// campus pastor, except for the types the church office writes for every campus. Never the
        /// noted person or someone in their family.
        /// </summary>
        private LoadedPerson PickAuthor( SeededRandom random, NoteTypeDefinition type, NoteContext context, NotesConfiguration configuration )
        {
            bool CanWrite( LoadedPerson author ) => author != null && author.FamilyId != context.Person.FamilyId;

            if ( !context.IsMainCampus )
            {
                var isOfficeType = configuration.CampusPastor.OfficeTypes.Contains( type.Key );
                var candidate = isOfficeType ? _staff.GetValueOrDefault( configuration.CampusPastor.OfficeAuthor ) : context.CampusPastor;
                if ( CanWrite( candidate ) )
                {
                    return candidate;
                }
            }

            var weights = type.Authors.Where( a => CanWrite( _staff.GetValueOrDefault( a.Key ) ) ).ToDictionary( a => a.Key, a => a.Value );
            return weights.Count > 0
                ? _staff[random.Pick( weights )]
                : _staff.Values.First( CanWrite );
        }

        /// <summary>
        /// An opener, then up to four details, aiming for a length drawn from the profile's percentiles.
        /// </summary>
        private static string Compose( SeededRandom random, Design design, NotePoolSection section, NoteContext context )
        {
            var target = design.DrawLength( random );
            var text = new StringBuilder( Fill( random.Pick( section.Openers ), context ) );
            var used = new HashSet<int>();
            while ( text.Length < target && used.Count < Math.Min( MaxDetails, section.Details.Count ) )
            {
                var index = random.Between( 0, section.Details.Count - 1 );
                if ( used.Add( index ) )
                {
                    text.Append( ' ' ).Append( Fill( section.Details[index], context ) );
                }
            }

            return text.ToString();
        }

        /// <summary>
        /// Fills a pool entry's tokens for the noted person. {staff} is left for the mention.
        /// </summary>
        private static string Fill( string template, NoteContext context )
        {
            var person = context.Person.Cast;
            var (he, him, his) = person.Gender switch
            {
                "male" => ("he", "him", "his"),
                "female" => ("she", "her", "her"),
                _ => ("they", "them", "their")
            };

            return template
                .Replace( "{name}", person.NickName ?? person.FirstName )
                .Replace( "{campus}", context.CampusName )
                .Replace( "{he}", he ).Replace( "{He}", Capitalize( he ) )
                .Replace( "{him}", him )
                .Replace( "{his}", his ).Replace( "{His}", Capitalize( his ) );
        }

        private static string Capitalize( string word ) => char.ToUpperInvariant( word[0] ) + word.Substring( 1 );

        #endregion Generated notes

        #region Writing

        /// <summary>
        /// Rock's note editor stores its content as HTML, so the text escapes what HTML would read as markup.
        /// </summary>
        private static string Escape( string text ) => text.Replace( "&", "&amp;" ).Replace( "<", "&lt;" ).Replace( ">", "&gt;" );

        /// <summary>
        /// A staff mention as Rock's note editor writes it, or the plain name when the loader has no
        /// DataEncryptionKey, because the mention's identifier is an IdKey keyed by it.
        /// </summary>
        private string Mention( LoadedPerson staff )
        {
            var name = $"{staff.Cast.NickName ?? staff.Cast.FirstName} {staff.Cast.LastName}";
            var identifier = _keys?.IdKey( staff.AliasId );
            return identifier == null ? name : $"<span class=\"mention\" data-identifier=\"{identifier}\">@{name}</span>";
        }

        private int Add( NoteRow row )
        {
            _rows.Add( row );
            return _rows.Count - 1;
        }

        /*
            10/20/26 - CLAUDE

            NotificationsSent and ApprovalsSent are set on every note. Rock's Send Note Notifications job
            emails watchers and mentioned people about notes where they are false, and the first run after
            an install would otherwise email staff about years of sample notes. EditedDateTime matches the
            created time, because Rock's note editor sets it on every save, including the first.

            Reason: Loaded notes look like notes written in Rock, and nobody gets old notifications.
        */

        /// <summary>
        /// Writes every note with explicit Ids, so replies can point at their parents.
        /// </summary>
        private void Insert()
        {
            var nextId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [Note]" ) );
            var table = TableFactory.Create( "Id:int", "IsSystem:bool", "NoteTypeId:int", "EntityId:int", "Caption:string", "IsAlert:bool", "Text:string", "Guid:guid",
                "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int", "ForeignKey:string", "IsPrivateNote:bool",
                "ParentNoteId:int", "ApprovalStatus:int", "NotificationsSent:bool", "ApprovalsSent:bool", "EditedDateTime:date", "EditedByPersonAliasId:int", "IsPinned:bool" );

            var ids = new int[_rows.Count];
            for ( var i = 0; i < _rows.Count; i++ )
            {
                var row = _rows[i];
                ids[i] = nextId++;
                table.Rows.Add( ids[i], false, _typeIds[row.TypeKey], row.PersonId, row.IsPrivate ? PrivateCaption : string.Empty, row.IsAlert, row.Text, row.Guid,
                    row.Written, row.Written, row.AuthorAliasId, row.AuthorAliasId, _foreignKey, row.IsPrivate,
                    row.ParentIndex.HasValue ? ids[row.ParentIndex.Value] : DBNull.Value, ApprovedStatus, true, true, row.Written, row.AuthorAliasId, row.IsPinned );
            }

            _db.BulkInsert( "Note", table, keepIdentity: true );
        }

        #endregion Writing

        /// <summary>
        /// One note or reply waiting to be written.
        /// </summary>
        private sealed class NoteRow
        {
            public Guid Guid { get; set; }

            public string TypeKey { get; set; }

            public int PersonId { get; set; }

            public int AuthorAliasId { get; set; }

            public DateTime Written { get; set; }

            public string Text { get; set; }

            public bool IsAlert { get; set; }

            public bool IsPrivate { get; set; }

            public bool IsPinned { get; set; }

            /// <summary>
            /// The index of the note this one replies to, or null for a top-level note.
            /// </summary>
            public int? ParentIndex { get; set; }
        }

        /// <summary>
        /// The person a generated note is about, and who writes for their campus.
        /// </summary>
        private sealed class NoteContext
        {
            public LoadedPerson Person { get; set; }

            public string Audience { get; set; }

            public string CampusName { get; set; }

            public LoadedPerson CampusPastor { get; set; }

            public bool IsMainCampus { get; set; }
        }

        /// <summary>
        /// The profile's personNotes targets: the measured shape and the designed volumes.
        /// </summary>
        private sealed class Design
        {
            public static readonly string[] Weekdays = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

            private readonly Dictionary<string, double> _pctNoted;
            private readonly Dictionary<string, double> _pctPrivate;
            private readonly double _lengthP50;
            private readonly double _lengthSpread;

            public Design( ProfileTargets profile )
            {
                _pctNoted = profile.Map( "personNotes.design.pctNotedIn24MonthsByStatus" );
                _pctPrivate = profile.Map( "personNotes.design.pctPrivateByType" );
                NotesPerPersonMix = Require( profile.Map( "personNotes.design.notesPerNotedPersonMix" ), "personNotes.design.notesPerNotedPersonMix" );
                OlderNotesPerYearRatio = profile.Number( "personNotes.design.olderNotesPerYearRatio", 0.5 );
                PctPersonalNotesThatAreAlerts = profile.Number( "personNotes.design.pctPersonalNotesThatAreAlerts", 0 );
                PctNotedPeopleWithPinnedNote = profile.Number( "personNotes.design.pctNotedPeopleWithPinnedNote", 0 );
                PctTopLevelNotesWithReply = profile.Number( "personNotes.design.pctTopLevelNotesWithReply", 0 );
                PctRepliesWithSecondReply = profile.Number( "personNotes.design.pctRepliesWithSecondReply", 0 );
                PctNotesWithMention = profile.Number( "personNotes.design.pctNotesWithMention", 0 );
                WeekdayMix = Require( profile.Map( "personNotes.weekdayMix" ), "personNotes.weekdayMix" );
                HourBandMix = Require( profile.Map( "personNotes.hourBandMix" ), "personNotes.hourBandMix" );

                // Lengths follow a log-normal fitted to the median and 90th percentile (z = 1.2816 at P90).
                _lengthP50 = profile.Number( "personNotes.detail.textLengthP50", 110 );
                _lengthSpread = Math.Log( profile.Number( "personNotes.detail.textLengthP90", 260 ) / _lengthP50 ) / 1.2816;
            }

            public Dictionary<string, double> NotesPerPersonMix { get; }

            public double OlderNotesPerYearRatio { get; }

            public double PctPersonalNotesThatAreAlerts { get; }

            public double PctNotedPeopleWithPinnedNote { get; }

            public double PctTopLevelNotesWithReply { get; }

            public double PctRepliesWithSecondReply { get; }

            public double PctNotesWithMention { get; }

            public Dictionary<string, double> WeekdayMix { get; }

            public Dictionary<string, double> HourBandMix { get; }

            public double PctNoted( string status ) => _pctNoted.GetValueOrDefault( status, 0 );

            public double PctPrivate( string typeKey ) => _pctPrivate.GetValueOrDefault( typeKey, 0 );

            /// <summary>
            /// A target text length. SeededRandom.LogNormal has a mean of 1, so it is scaled to a median of 1.
            /// </summary>
            public double DrawLength( SeededRandom random ) => _lengthP50 * random.LogNormal( _lengthSpread ) * Math.Exp( _lengthSpread * _lengthSpread / 2 );

            private static Dictionary<string, double> Require( Dictionary<string, double> map, string path )
            {
                return map.Count > 0 ? map : throw new InvalidOperationException( $"The profile has no {path}." );
            }
        }
    }
}
