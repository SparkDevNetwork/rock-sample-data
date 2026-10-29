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
using System.Globalization;

using RockSampleData.Loader.Database;
using RockSampleData.Loader.Generation;

namespace RockSampleData.Loader.Loading
{
    /*
        10/29/26 - CLAUDE

        These are the notes and history rows Rock's registration entry writes when someone registers (Rock.Blocks/Event/
        RegistrationEntry.cs and Registration.SavePersonNotesAndHistory, the same in Rock 20 and 21):

        1. The registration's history, in the Event Registration category: "Added Registration", then the registrar's
           first name, last name, and email, the discount code, percent, and amount, the linked group, and the campus,
           each as Rock's History.EvaluateChange words it. The campus is the linkage's, or else the registrar's family's.
        2. Each registrant's history on the registration, captioned "Registrant: <name>": a person attribute the form
           saved to the person, each fee added with its quantity and cost, and each registrant attribute answered.
        3. "Added Payment" on the registration for each payment made on the website. A payment plan's payments come
           through Rock's job, which writes none.
        4. On a template with Add Person Note (every template here), a note of Rock's Event Registration type on each
           registrant, "Registered for <instance>", with " by <registrar>" when someone else registered them, and a
           "Registered" line on their history. Rock compares the registrar's first name with their nick name and, when
           they differ, names the registrar by first and last name, even on their own note, so that quirk is kept.
           The registrar also gets a note and history line naming the others, "Registered A and B  for <instance>",
           with Rock's double space.

        Everything is dated the moment the registration was saved, and written by the registrar, who is logged in.

        Reason: Decision D-registrations-15. The registration's History tab and the person's Notes and History tabs
        read the way Rock wrote them.
    */

    /// <summary>
    /// Writes the notes and history rows Rock's registration entry writes for each registration and payment.
    /// </summary>
    internal sealed class RegistrationHistoryWriter
    {
        private static readonly Guid EventRegistrationNoteTypeGuid = new Guid( "BBADA8EF-23FC-4B46-B7A7-0F6D31F8C045" );
        private static readonly Guid RegistrationHistoryCategoryGuid = new Guid( "813DF1A5-ADBD-481C-AC1D-884F0FA7AE77" );
        private static readonly Guid PersonRegistrationHistoryCategoryGuid = new Guid( "DA9C0CC7-7B31-4E1E-BBA5-50405B2D9EFE" );
        private static readonly Guid PersonEntityTypeGuid = new Guid( "72657ED8-D16E-492E-AC12-144C5E7567E7" );
        private static readonly Guid RegistrationEntityTypeGuid = new Guid( "D2F294C6-E161-4A56-85C7-CD74D535F61A" );

        /// <summary>
        /// Rock's NoteApprovalStatus.Approved, which the note save hook sets on a type that needs no approval.
        /// </summary>
        private const int NoteApproved = 1;

        private static readonly CultureInfo UnitedStates = CultureInfo.GetCultureInfo( "en-US" );

        private readonly RockDatabase _db;
        private readonly string _foreignKey;

        private readonly DataTable _notes = TableFactory.Create( "Id:int", "IsSystem:bool", "NoteTypeId:int", "EntityId:int", "Caption:string", "IsAlert:bool", "IsPrivateNote:bool", "Text:string",
            "ApprovalStatus:int", "NotificationsSent:bool", "ApprovalsSent:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date", "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int" );

        private readonly DataTable _history = TableFactory.Create( "IsSystem:bool", "CategoryId:int", "EntityTypeId:int", "EntityId:int", "Caption:string", "RelatedEntityTypeId:int", "RelatedEntityId:int",
            "Verb:string", "ChangeType:string", "ValueName:string", "OldValue:string", "NewValue:string", "IsSensitive:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date",
            "CreatedByPersonAliasId:int", "ModifiedByPersonAliasId:int" );

        private int _nextNoteId;
        private int _registrationCategoryId;
        private int _personCategoryId;
        private int _personEntityTypeId;
        private int _registrationEntityTypeId;

        public RegistrationHistoryWriter( RockDatabase db, string foreignKey )
        {
            _db = db;
            _foreignKey = foreignKey;
        }

        public int Notes => _notes.Rows.Count;

        public int HistoryRows => _history.Rows.Count;

        /// <summary>
        /// Writes the notes and history of every registration.
        /// </summary>
        /// <param name="savedPersonAnswers">The person attribute values each registrant's form saved to them, by registrant.</param>
        public void Write( List<PlannedRegistration> registrations, Dictionary<PlannedRegistration, int> registrationIds, RegistrationIds ids, ILookup<PlannedRegistrant, (Guid Attribute, string Value)> savedPersonAnswers )
        {
            var noteTypeId = _db.RequireIdByGuid( "NoteType", EventRegistrationNoteTypeGuid );
            _registrationCategoryId = _db.RequireIdByGuid( "Category", RegistrationHistoryCategoryGuid );
            _personCategoryId = _db.RequireIdByGuid( "Category", PersonRegistrationHistoryCategoryGuid );
            _personEntityTypeId = _db.RequireIdByGuid( "EntityType", PersonEntityTypeGuid );
            _registrationEntityTypeId = _db.RequireIdByGuid( "EntityType", RegistrationEntityTypeGuid );
            _nextNoteId = Convert.ToInt32( _db.Scalar( "SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [Note]" ) );
            var campusNames = _db.Query( "SELECT [Id], [Name] FROM [Campus]" ).ToDictionary( r => Convert.ToInt32( r["Id"] ), r => ( string ) r["Name"] );
            var attributeNames = savedPersonAnswers.SelectMany( g => g ).Select( a => a.Attribute ).Distinct()
                .ToDictionary( g => g, g => ( string ) _db.Scalar( "SELECT [Name] FROM [Attribute] WHERE [Guid] = @Guid", RockDatabase.P( "@Guid", g ) ) );

            foreach ( var registration in registrations )
            {
                var id = registrationIds[registration];
                var saved = registration.When.AddSeconds( 1 );
                var registrar = registration.Registrar;
                RegistrationChanges( registration, id, saved, ids, campusNames );

                foreach ( var registrant in registration.Registrants )
                {
                    RegistrantChanges( registration, registrant, id, saved, attributeNames, savedPersonAnswers[registrant] );
                }

                for ( var i = 0; i < registration.Payments.Count; i++ )
                {
                    var payment = registration.Payments[i];
                    if ( !payment.IsScheduled )
                    {
                        Change( _registrationCategoryId, _registrationEntityTypeId, id, null, null, "ADD", "Record", "Payment", null, $"{payment.Amount.ToString( "C", UnitedStates )} payment",
                            payment.When, registrar.AliasId, $"{registration.Guid}:history:payment:{i}" );
                    }
                }

                // Every template turns on Add Person Note (RegistrationsLoader), so every registration writes its notes.
                PersonNotes( registration, id, saved, noteTypeId );
            }

            _db.BulkInsert( "Note", _notes, keepIdentity: true );
            _db.BulkInsert( "History", _history, keepIdentity: false );
        }

        /// <summary>
        /// The registration's own history, as RegistrationEntry builds registrationChanges for a new registration.
        /// </summary>
        private void RegistrationChanges( PlannedRegistration registration, int id, DateTime saved, RegistrationIds ids, Dictionary<int, string> campusNames )
        {
            var registrar = registration.Registrar.Cast;
            var discount = registration.Discount;
            var changes = new List<(string Verb, string Type, string Name, string Old, string New)> { ("ADD", "Record", "Registration", null, null) };
            void Evaluate( string name, string oldValue, string newValue )
            {
                if ( !string.IsNullOrWhiteSpace( newValue ) && ( oldValue ?? string.Empty ).Trim() != newValue.Trim() )
                {
                    changes.Add( ("MODIFY", "Property", name, string.IsNullOrWhiteSpace( oldValue ) ? null : oldValue, newValue) );
                }
            }

            Evaluate( "First Name", null, registrar.NickName ?? registrar.FirstName );
            Evaluate( "Last Name", null, registrar.LastName );
            Evaluate( "Confirmation Email", null, registrar.Email );
            Evaluate( "Discount Code", null, discount?.Code );
            Evaluate( "Discount Percentage", Decimal( 0 ), Decimal( ( discount?.Percent ?? 0 ) / 100 ) );
            Evaluate( "Discount Amount", Decimal( 0 ), Decimal( discount?.Amount ?? 0 ) );
            if ( registration.Instance.LinkedGroup != null )
            {
                Evaluate( "Group", string.Empty, registration.Instance.LinkedGroup.Name );
            }

            Evaluate( "Campus", string.Empty, campusNames[CampusId( registration, ids )] );
            for ( var i = 0; i < changes.Count; i++ )
            {
                var change = changes[i];
                Change( _registrationCategoryId, _registrationEntityTypeId, id, null, null, change.Verb, change.Type, change.Name, change.Old, change.New,
                    saved, registration.Registrar.AliasId, $"{registration.Guid}:history:{i}" );
            }
        }

        /// <summary>
        /// The campus Rock's registration entry puts on a registration: the linkage's campus, or else the registrar's family's.
        /// </summary>
        internal static int CampusId( PlannedRegistration registration, RegistrationIds ids )
        {
            return registration.Instance.CampusKey != null ? ids.CampusPlans[registration.Instance.CampusKey].CampusId : registration.Registrar.CampusId;
        }

        /// <summary>
        /// One registrant's history on the registration: person attributes saved, fees, and registrant attributes.
        /// </summary>
        private void RegistrantChanges( PlannedRegistration registration, PlannedRegistrant registrant, int id, DateTime saved, Dictionary<Guid, string> attributeNames, IEnumerable<(Guid Attribute, string Value)> personAnswers )
        {
            var caption = $"Registrant: {FullName( registrant.Person )}";
            var changes = new List<(string Verb, string Type, string Name, string Old, string New)>();
            foreach ( var answer in personAnswers )
            {
                changes.Add( ("MODIFY", "Property", attributeNames[answer.Attribute], null, answer.Value) );
            }

            var template = registration.Instance.Template;
            foreach ( var fee in template.Fees )
            {
                foreach ( var chosen in registrant.Fees.Where( f => f.Fee == fee ).OrderBy( f => f.ItemIndex ) )
                {
                    var feeName = $"{fee.Name} ({fee.Items[chosen.ItemIndex].Name})";
                    changes.Add( ("ADD", "Record", "Fee", null, feeName) );
                    if ( chosen.Quantity != 0 )
                    {
                        changes.Add( ("MODIFY", "Property", $"{feeName} Quantity", "0", chosen.Quantity.ToString( UnitedStates )) );
                    }

                    if ( chosen.Cost != 0 )
                    {
                        changes.Add( ("MODIFY", "Property", $"{feeName} Cost", Decimal( 0 ), Decimal( chosen.Cost )) );
                    }
                }
            }

            foreach ( var field in template.Forms.SelectMany( f => f.Fields ).Where( f => f.Attribute != null ) )
            {
                if ( registrant.Answers.TryGetValue( field.Attribute, out var value ) && !string.IsNullOrWhiteSpace( value ) )
                {
                    changes.Add( ("MODIFY", "Property", template.Attributes.First( a => a.Key == field.Attribute ).Name, null, value) );
                }
            }

            for ( var i = 0; i < changes.Count; i++ )
            {
                var change = changes[i];
                Change( _registrationCategoryId, _registrationEntityTypeId, id, caption, null, change.Verb, change.Type, change.Name, change.Old, change.New,
                    saved, registration.Registrar.AliasId, $"{registrant.Guid}:history:{i}" );
            }
        }

        /// <summary>
        /// Registration.SavePersonNotesAndHistory for a new registration: a note and a history line on each registrant,
        /// then on the registrar for the others they registered.
        /// </summary>
        private void PersonNotes( PlannedRegistration registration, int id, DateTime saved, int noteTypeId )
        {
            var registrar = registration.Registrar;
            var instanceName = registration.Instance.Name;
            var names = new List<string>();
            var registrarFirstName = registrar.Cast.FirstName;
            var isNickNameDifferent = registrarFirstName != ( registrar.Cast.NickName ?? registrar.Cast.FirstName );
            foreach ( var registrant in registration.Registrants )
            {
                var by = string.Empty;
                if ( registrant.Person.PersonId != registrar.PersonId )
                {
                    by = $" by {FullName( registrar )}";
                    names.Add( FullName( registrant.Person ) );
                }

                if ( isNickNameDifferent )
                {
                    by = $" by {registrarFirstName} {registrar.Cast.LastName}";
                }

                Note( noteTypeId, registrant.Person.PersonId, instanceName, $"Registered for {instanceName}{by}", saved, registrar.AliasId, $"{registrant.Guid}:note" );
                Change( _personCategoryId, _personEntityTypeId, registrant.Person.PersonId, instanceName, id, "REGISTERED", "Record", null, null, null,
                    saved, registrar.AliasId, $"{registrant.Guid}:person-history" );
            }

            if ( names.Count == 0 )
            {
                return;
            }

            var namesText = names.Count >= 2 ? $"{string.Join( ", ", names.Take( names.Count - 1 ) )} and {names[names.Count - 1]} " : $"{names[0]} ";
            Note( noteTypeId, registrar.PersonId, instanceName, $"Registered {namesText} for {instanceName}", saved, registrar.AliasId, $"{registration.Guid}:registrar-note" );
            Change( _personCategoryId, _personEntityTypeId, registrar.PersonId, instanceName, id, "REGISTERED", "Record", namesText, null, null,
                saved, registrar.AliasId, $"{registration.Guid}:registrar-person-history" );
        }

        private void Note( int noteTypeId, int personId, string caption, string text, DateTime when, int authorAliasId, string guidKey )
        {
            _notes.Rows.Add( _nextNoteId++, false, noteTypeId, personId, caption, false, false, text, NoteApproved, true, true, SeededRandom.StableGuid( guidKey ), _foreignKey, when, when, authorAliasId, authorAliasId );
        }

        private void Change( int categoryId, int entityTypeId, int entityId, string caption, int? relatedRegistrationId, string verb, string changeType, string valueName, string oldValue, string newValue,
            DateTime when, int byAliasId, string guidKey )
        {
            _history.Rows.Add( false, categoryId, entityTypeId, entityId, ( object ) caption ?? DBNull.Value, relatedRegistrationId.HasValue ? _registrationEntityTypeId : DBNull.Value,
                ( object ) relatedRegistrationId ?? DBNull.Value, verb, changeType, ( object ) valueName ?? DBNull.Value, ( object ) oldValue ?? DBNull.Value, ( object ) newValue ?? DBNull.Value, false,
                SeededRandom.StableGuid( guidKey ), _foreignKey, when, when, byAliasId, byAliasId );
        }

        /// <summary>
        /// A decimal as History.EvaluateChange writes it: "N2" in the server's culture, US English.
        /// </summary>
        private static string Decimal( decimal value ) => value.ToString( "N2", UnitedStates );

        private static string FullName( LoadedPerson person ) => $"{person.Cast.NickName ?? person.Cast.FirstName} {person.Cast.LastName}";
    }
}
