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
using RockSampleData.Loader.Model;

namespace RockSampleData.Loader.Loading
{
    /*
        10/28/26 - CLAUDE

        Registrations are written the way Rock's registration entry block saves them (Rock.Blocks/Event/RegistrationEntry.cs):
        the registration with the registrar's name and email and the linkage's group and campus, each registrant with
        the template's cost and their fees, and each registrant not on the wait list added to the linked group with the
        template's role and status. A payment is an Event Registration transaction from the registrar, from the Website,
        through the template's gateway, whose one detail points at the registration and posts to the instance's
        account. It lands in the day's batch that FinancialBatchService.GetForNewTransaction finds or makes: the
        template's batch prefix and the card type's batch suffix (or the currency), and the batch's control amount
        grows with each payment. A payment plan's payments come through Rock's Download Payments job, which uses its own
        batch prefix. Placed people are members of their placement groups with the group type's default role.

        Reason: Rock's registration screens, balances, batches, and placement board read these rows as if people had registered (decision D-registrations-10).
    */

    /// <summary>
    /// Writes the planned registrations: registrations, registrants, fees, attribute values, group members, and
    /// payments with their batches, payment details, and payment plans.
    /// </summary>
    internal sealed class RegistrantsLoader
    {
        private static readonly Guid EventRegistrationTypeGuid = new Guid( "33CB96DD-8752-4BEE-A142-88DB7DE538F0" );
        private static readonly Guid RegistrationEntityTypeGuid = new Guid( "D2F294C6-E161-4A56-85C7-CD74D535F61A" );

        private static readonly Dictionary<PaymentPlanFrequency, Guid> FrequencyGuids = new Dictionary<PaymentPlanFrequency, Guid>
        {
            [PaymentPlanFrequency.Monthly] = new Guid( "1400753C-A0F9-4A45-8A1D-81C98450BD1F" ),
            [PaymentPlanFrequency.TwiceMonthly] = new Guid( "791C863D-2600-445B-98F8-3E5B66A3DEC4" )
        };

        /// <summary>
        /// The batch prefix of Rock's Download Payments job, which records each payment of a payment plan.
        /// </summary>
        private const string DownloadPaymentsBatchPrefix = "Online Giving";

        /// <summary>
        /// Rock's BatchStatus values. A batch older than a week has been closed.
        /// </summary>
        private const int BatchOpen = 1;
        private const int BatchClosed = 2;
        private const int OpenBatchDays = 7;

        private readonly RockDatabase _db;
        private readonly string _foreignKey;
        private readonly string _seed;
        private readonly AnchorDates _dates;
        private readonly PaymentMethods _payments;

        public RegistrantsLoader( RockDatabase db, string foreignKey, string seed, AnchorDates dates )
        {
            _db = db;
            _foreignKey = foreignKey;
            _seed = seed;
            _dates = dates;
            _payments = new PaymentMethods( db );
        }

        public void Write( List<PlannedRegistration> registrations, RegistrationIds ids )
        {
            var registrationIds = new Dictionary<PlannedRegistration, int>();
            var nextId = NextId( "Registration" );
            foreach ( var registration in registrations )
            {
                registrationIds[registration] = nextId++;
            }

            var memberIds = WriteGroupMembers( registrations, ids );
            var planIds = WritePayments( registrations, registrationIds, ids );
            WriteRegistrations( registrations, registrationIds, planIds, ids );
            WriteRegistrants( registrations, registrationIds, memberIds, ids );
            WritePersonAnswers( registrations );

            var registrants = registrations.SelectMany( r => r.Registrants ).ToList();
            var dollars = registrations.Sum( r => r.Paid );
            _db.Log.Write( $"  Registrants: {registrations.Count:N0} registrations, {registrants.Count:N0} registrants ({registrants.Count( r => r.OnWaitList ):N0} on wait lists, {registrants.Count( r => r.Placements.Count > 0 ):N0} placed); "
                + $"{registrations.Sum( r => r.Payments.Count ):N0} payments (${dollars:N0}), {registrations.Count( r => r.Plan != null ):N0} payment plans, {registrations.Count( r => r.DiscountedCost > r.Paid ):N0} with a balance due" );
        }

        /// <summary>
        /// Adds each registrant not on the wait list to the linked group with the template's role and status, and each
        /// placed registrant to their placement groups with the group type's default role. Returns the linked group
        /// member Id of each registrant.
        /// </summary>
        private Dictionary<PlannedRegistrant, int> WriteGroupMembers( List<PlannedRegistration> registrations, RegistrationIds ids )
        {
            var nextId = NextId( "GroupMember" );
            var memberIds = new Dictionary<PlannedRegistrant, int>();
            var rows = TableFactory.Create( "Id:int", "IsSystem:bool", "GroupId:int", "GroupTypeId:int", "PersonId:int", "GroupRoleId:int", "GroupMemberStatus:int", "DateTimeAdded:date",
                "CommunicationPreference:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            foreach ( var registration in registrations )
            {
                var instance = registration.Instance;
                var template = instance.Template;
                foreach ( var registrant in registration.Registrants.Where( r => !r.OnWaitList ) )
                {
                    if ( instance.LinkedGroup != null )
                    {
                        var type = ids.GroupTypes[template.GroupType];
                        var roleId = type.RoleIds.TryGetValue( template.GroupMemberRole ?? string.Empty, out var role ) ? role : type.DefaultRoleId;
                        memberIds[registrant] = nextId;
                        rows.Rows.Add( nextId++, false, ids.GroupIds[instance.LinkedGroup.Key], type.Id, registrant.Person.PersonId, roleId, ( int ) template.GroupMemberStatus, registration.When,
                            0, SeededRandom.StableGuid( $"{registrant.Guid}:linked-group" ), _foreignKey, registration.When, registration.When );
                    }

                    foreach ( var placement in registrant.Placements )
                    {
                        var type = ids.GroupTypes[placement.Group.Placement.GroupType];
                        rows.Rows.Add( nextId++, false, ids.GroupIds[placement.Group.Group.Key], type.Id, registrant.Person.PersonId, type.DefaultRoleId, 1, placement.On,
                            0, SeededRandom.StableGuid( $"{registrant.Guid}:placement:{placement.Group.Group.Key}" ), _foreignKey, placement.On, placement.On );
                    }
                }
            }

            _db.BulkInsert( "GroupMember", rows, keepIdentity: true );
            return memberIds;
        }

        private void WriteRegistrations( List<PlannedRegistration> registrations, Dictionary<PlannedRegistration, int> registrationIds, Dictionary<PlannedRegistration, int> planIds, RegistrationIds ids )
        {
            var rows = TableFactory.Create( "Id:int", "RegistrationInstanceId:int", "RegistrationTemplateId:int", "PersonAliasId:int", "FirstName:string", "LastName:string", "ConfirmationEmail:string", "GroupId:int",
                "DiscountCode:string", "DiscountPercentage:decimal", "DiscountAmount:decimal", "IsTemporary:bool", "LastPaymentReminderDateTime:date", "CreatedDateKey:int", "CampusId:int",
                "PaymentPlanFinancialScheduledTransactionId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var values = AttributeValueTable();
            foreach ( var registration in registrations )
            {
                var instance = registration.Instance;
                var templateIds = ids.Templates[instance.Template.Key];
                var registrar = registration.Registrar.Cast;
                var discount = registration.Discount;
                var modified = registration.LastPaymentReminder > registration.When ? registration.LastPaymentReminder.Value : registration.When;
                var id = registrationIds[registration];
                rows.Rows.Add( id, ids.InstanceIds[instance], templateIds.Id, registration.Registrar.AliasId, registrar.NickName ?? registrar.FirstName, registrar.LastName, ( object ) registrar.Email ?? DBNull.Value,
                    instance.LinkedGroup == null ? DBNull.Value : ids.GroupIds[instance.LinkedGroup.Key], ( object ) discount?.Code ?? DBNull.Value, ( discount?.Percent ?? 0 ) / 100, discount?.Amount ?? 0, false,
                    ( object ) registration.LastPaymentReminder ?? DBNull.Value, DateKey( registration.When ), instance.CampusKey == null ? DBNull.Value : ids.CampusPlans[instance.CampusKey].CampusId,
                    planIds.TryGetValue( registration, out var planId ) ? planId : DBNull.Value, registration.Guid, _foreignKey, registration.When, modified );

                foreach ( var answer in registration.Answers )
                {
                    values.Rows.Add( false, templateIds.RegistrationAttributeIds[answer.Key], id, answer.Value, true, SeededRandom.StableGuid( $"{registration.Guid}:attribute:{answer.Key}" ), _foreignKey, registration.When, registration.When );
                }
            }

            _db.BulkInsert( "Registration", rows, keepIdentity: true );
            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );
        }

        private void WriteRegistrants( List<PlannedRegistration> registrations, Dictionary<PlannedRegistration, int> registrationIds, Dictionary<PlannedRegistrant, int> memberIds, RegistrationIds ids )
        {
            var nextId = NextId( "RegistrationRegistrant" );
            var rows = TableFactory.Create( "Id:int", "RegistrationId:int", "RegistrationTemplateId:int", "PersonAliasId:int", "GroupMemberId:int", "Cost:decimal", "OnWaitList:bool", "DiscountApplies:bool",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var fees = TableFactory.Create( "RegistrationRegistrantId:int", "RegistrationTemplateFeeId:int", "RegistrationTemplateFeeItemId:int", "Option:string", "Quantity:int", "Cost:decimal",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var values = AttributeValueTable();
            foreach ( var registration in registrations )
            {
                var templateIds = ids.Templates[registration.Instance.Template.Key];
                foreach ( var registrant in registration.Registrants )
                {
                    var id = nextId++;
                    var when = registration.When;
                    rows.Rows.Add( id, registrationIds[registration], templateIds.Id, registrant.Person.AliasId, memberIds.TryGetValue( registrant, out var memberId ) ? memberId : DBNull.Value,
                        registrant.Cost, registrant.OnWaitList, registrant.DiscountApplies, registrant.Guid, _foreignKey, when, when );

                    // Rock's placement board joins on the fee item, so every fee names its item.
                    foreach ( var fee in registrant.Fees )
                    {
                        fees.Rows.Add( id, templateIds.FeeIds[fee.Fee.Key], templateIds.FeeItemIds[(fee.Fee.Key, fee.ItemIndex)], fee.Fee.Items[fee.ItemIndex].Name, fee.Quantity, fee.Cost,
                            SeededRandom.StableGuid( $"{registrant.Guid}:fee:{fee.Fee.Key}" ), _foreignKey, when, when );
                    }

                    foreach ( var answer in registrant.Answers )
                    {
                        values.Rows.Add( false, templateIds.RegistrantAttributeIds[answer.Key], id, answer.Value, true, SeededRandom.StableGuid( $"{registrant.Guid}:attribute:{answer.Key}" ), _foreignKey, when, when );
                    }
                }
            }

            _db.BulkInsert( "RegistrationRegistrant", rows, keepIdentity: true );
            _db.BulkInsert( "RegistrationRegistrantFee", fees, keepIdentity: false );
            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );
        }

        /*
            10/29/26 - CLAUDE

            An earlier step may already have saved a person's value for the same attribute, such as the people step
            writing a Cast child's Allergy from their file. Rock keeps one value per person and attribute, and its form
            shows that current value, so a registration keeps it and writes nothing.

            Reason: Registrations and the people step never write the same person value twice.
        */

        /// <summary>
        /// Saves each person attribute a registration form answered to the person, once, from their first registration
        /// that answered it, as Rock's registration entry saves it. A person who already has a value keeps it.
        /// </summary>
        private void WritePersonAnswers( List<PlannedRegistration> registrations )
        {
            var values = AttributeValueTable();
            var attributeIds = new Dictionary<Guid, int>();
            var answers = registrations.OrderBy( r => r.When ).ThenBy( r => r.Guid )
                .SelectMany( r => r.Registrants.SelectMany( p => p.PersonAnswers.Select( a => (r.When, p.Person, Attribute: a.Key, a.Value) ) ) )
                .GroupBy( a => (a.Attribute, a.Person.PersonId) )
                .Select( g => g.First() )
                .ToList();
            foreach ( var attribute in answers.Select( a => a.Attribute ).Distinct() )
            {
                attributeIds[attribute] = _db.RequireIdByGuid( "Attribute", attribute );
            }

            var saved = attributeIds.Count == 0 ? new HashSet<(int, int)>()
                : _db.Query( $"SELECT [AttributeId], [EntityId] FROM [AttributeValue] WHERE [AttributeId] IN ( {string.Join( ", ", attributeIds.Values )} ) AND [EntityId] IS NOT NULL" )
                    .Select( r => (Convert.ToInt32( r["AttributeId"] ), Convert.ToInt32( r["EntityId"] )) )
                    .ToHashSet();
            foreach ( var answer in answers )
            {
                var attributeId = attributeIds[answer.Attribute];
                if ( saved.Contains( (attributeId, answer.Person.PersonId) ) )
                {
                    continue;
                }

                values.Rows.Add( false, attributeId, answer.Person.PersonId, answer.Value, true, SeededRandom.StableGuid( $"{answer.Person.Cast.Guid}:person-attribute:{answer.Attribute}" ), _foreignKey, answer.When, answer.When );
            }

            _db.BulkInsert( "AttributeValue", values, keepIdentity: false );
        }

        /// <summary>
        /// Writes every payment, its payment detail and batch, and each payment plan. Returns each plan's Id by registration.
        /// </summary>
        private Dictionary<PlannedRegistration, int> WritePayments( List<PlannedRegistration> registrations, Dictionary<PlannedRegistration, int> registrationIds, RegistrationIds ids )
        {
            var eventRegistrationId = _payments.DefinedValueId( EventRegistrationTypeGuid );
            var websiteId = _payments.DefinedValueId( PaymentMethods.WebsiteSourceGuid );
            var registrationEntityTypeId = _db.RequireIdByGuid( "EntityType", RegistrationEntityTypeGuid );
            var suffixes = BatchSuffixes();

            var batches = TableFactory.Create( "Id:int", "Name:string", "BatchStartDateTime:date", "BatchEndDateTime:date", "Status:int", "ControlAmount:decimal", "IsAutomated:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var paymentDetails = PaymentMethods.PaymentDetailTable();
            var schedules = TableFactory.Create( "Id:int", "TransactionFrequencyValueId:int", "StartDate:date", "EndDate:date", "NumberOfPayments:int", "NextPaymentDate:date", "IsActive:bool", "AuthorizedPersonAliasId:int",
                "FinancialGatewayId:int", "FinancialPaymentDetailId:int", "SourceTypeValueId:int", "TransactionTypeValueId:int", "TransactionCode:string", "GatewayScheduleId:string", "Summary:string",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var scheduleDetails = TableFactory.Create( "Id:int", "ScheduledTransactionId:int", "AccountId:int", "Amount:decimal", "EntityTypeId:int", "EntityId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var transactions = TableFactory.Create( "Id:int", "BatchId:int", "TransactionDateTime:date", "TransactionCode:string", "TransactionTypeValueId:int", "SourceTypeValueId:int", "ScheduledTransactionId:int",
                "AuthorizedPersonAliasId:int", "FinancialGatewayId:int", "FinancialPaymentDetailId:int", "IsSettled:bool", "SettledDate:date", "ShowAsAnonymous:bool", "SundayDate:date", "TransactionDateKey:int", "Summary:string",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var details = TableFactory.Create( "Id:int", "TransactionId:int", "AccountId:int", "Amount:decimal", "EntityTypeId:int", "EntityId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            var nextBatchId = NextId( "FinancialBatch" );
            var nextPaymentId = NextId( "FinancialPaymentDetail" );
            var nextScheduleId = NextId( "FinancialScheduledTransaction" );
            var nextScheduleDetailId = NextId( "FinancialScheduledTransactionDetail" );
            var nextTransactionId = NextId( "FinancialTransaction" );
            var nextDetailId = NextId( "FinancialTransactionDetail" );
            var batchIds = new Dictionary<(DateTime Day, string Name), int>();
            var batchTotals = new Dictionary<int, decimal>();
            var planIds = new Dictionary<PlannedRegistration, int>();

            foreach ( var registration in registrations.Where( r => r.Payments.Count > 0 ) )
            {
                var registrationId = registrationIds[registration];
                var wallet = new Wallet( _seed, registration.Registrar.Cast.Guid );
                var random = new SeededRandom( $"{_seed}:registrations:payments:{registration.Guid}" );
                var summary = Summary( registration, registrationId, ids );
                int? scheduleId = null;
                if ( registration.Plan is { } plan )
                {
                    scheduleId = nextScheduleId++;
                    planIds[registration] = scheduleId.Value;
                    var paymentId = nextPaymentId++;
                    _payments.AddPaymentDetail( paymentDetails, paymentId, registration.Currency, wallet, SeededRandom.StableGuid( $"{registration.Guid}:plan-payment-detail" ), _foreignKey, registration.When, registration.When );
                    var remaining = plan.Dates.Where( d => d >= _dates.Anchor ).ToList();
                    var isActive = remaining.Count > 0;
                    var lastPaid = registration.Payments.Where( p => p.IsScheduled ).Select( p => p.When ).DefaultIfEmpty( registration.When ).Max();
                    schedules.Rows.Add( scheduleId, _payments.DefinedValueId( FrequencyGuids[plan.Frequency] ), plan.Start, isActive ? DBNull.Value : ( object ) plan.Dates.Last(), plan.Dates.Count,
                        isActive ? ( object ) remaining[0] : DBNull.Value, isActive, registration.Registrar.AliasId, ids.GatewayId, paymentId, websiteId, eventRegistrationId,
                        PaymentMethods.TransactionCode( random ), PaymentMethods.GatewayScheduleId( scheduleId.Value ), summary, SeededRandom.StableGuid( $"{registration.Guid}:plan" ), _foreignKey, registration.When, lastPaid );
                    scheduleDetails.Rows.Add( nextScheduleDetailId++, scheduleId, ids.AccountId, plan.AmountPerPayment, registrationEntityTypeId, registrationId,
                        SeededRandom.StableGuid( $"{registration.Guid}:plan-detail" ), _foreignKey, registration.When, registration.When );
                }

                for ( var i = 0; i < registration.Payments.Count; i++ )
                {
                    var payment = registration.Payments[i];
                    var day = payment.When.Date;
                    var name = $"{( payment.IsScheduled ? DownloadPaymentsBatchPrefix : ids.BatchPrefix )} {suffixes( registration.Currency, wallet )}";
                    if ( !batchIds.TryGetValue( (day, name), out var batchId ) )
                    {
                        batchId = nextBatchId++;
                        batchIds[(day, name)] = batchId;
                        batches.Rows.Add( batchId, name, day, day.AddDays( 1 ), day < _dates.Anchor.AddDays( -OpenBatchDays ) ? BatchClosed : BatchOpen, 0m, false,
                            SeededRandom.StableGuid( $"registration-batch:{_foreignKey}:{name}:{day:yyyy-MM-dd}" ), _foreignKey, payment.When, payment.When );
                    }

                    batchTotals[batchId] = batchTotals.GetValueOrDefault( batchId ) + payment.Amount;
                    var paymentId = nextPaymentId++;
                    var transactionId = nextTransactionId++;
                    _payments.AddPaymentDetail( paymentDetails, paymentId, registration.Currency, wallet, SeededRandom.StableGuid( $"{registration.Guid}:payment-detail:{i}" ), _foreignKey, payment.When, payment.When );
                    transactions.Rows.Add( transactionId, batchId, payment.When, PaymentMethods.TransactionCode( random ), eventRegistrationId, websiteId, payment.IsScheduled ? ( object ) scheduleId.Value : DBNull.Value,
                        registration.Registrar.AliasId, ids.GatewayId, paymentId, true, day.AddDays( 1 ), false, SundayCalendar.SundayOnOrAfter( day ), DateKey( day ),
                        payment.IsScheduled ? DBNull.Value : summary, SeededRandom.StableGuid( $"{registration.Guid}:payment:{i}" ), _foreignKey, payment.When, payment.When );
                    details.Rows.Add( nextDetailId++, transactionId, ids.AccountId, payment.Amount, registrationEntityTypeId, registrationId,
                        SeededRandom.StableGuid( $"{registration.Guid}:payment-line:{i}" ), _foreignKey, payment.When, payment.When );
                }
            }

            foreach ( DataRow row in batches.Rows )
            {
                row["ControlAmount"] = batchTotals[( int ) row["Id"]];
            }

            _db.BulkInsert( "FinancialBatch", batches, keepIdentity: true );
            _db.BulkInsert( "FinancialPaymentDetail", paymentDetails, keepIdentity: true );
            _db.BulkInsert( "FinancialScheduledTransaction", schedules, keepIdentity: true );
            _db.BulkInsert( "FinancialScheduledTransactionDetail", scheduleDetails, keepIdentity: true );
            _db.BulkInsert( "FinancialTransaction", transactions, keepIdentity: true );
            _db.BulkInsert( "FinancialTransactionDetail", details, keepIdentity: true );
            return planIds;
        }

        /// <summary>
        /// The batch name's suffix for a payment, as FinancialBatchService names a batch: the card type's Batch Name
        /// Suffix, or its name, or the currency's name.
        /// </summary>
        private Func<string, Wallet, string> BatchSuffixes()
        {
            var cardSuffixes = _db.Query( @"
SELECT [dv].[Value], [av].[Value] AS [Suffix]
FROM [DefinedValue] AS [dv]
INNER JOIN [DefinedType] AS [dt] ON [dt].[Id] = [dv].[DefinedTypeId]
LEFT JOIN [Attribute] AS [a] ON [a].[Key] = 'BatchNameSuffix' AND [a].[EntityTypeQualifierColumn] = 'DefinedTypeId' AND [a].[EntityTypeQualifierValue] = CAST( [dt].[Id] AS NVARCHAR( 200 ) )
LEFT JOIN [AttributeValue] AS [av] ON [av].[AttributeId] = [a].[Id] AND [av].[EntityId] = [dv].[Id]
WHERE [dt].[Name] = 'Credit Card Type'" )
                .ToDictionary( r => ( string ) r["Value"], r => r["Suffix"] is string suffix && !string.IsNullOrWhiteSpace( suffix ) ? suffix : ( string ) r["Value"] );
            return ( currency, wallet ) => currency == "Credit Card" && cardSuffixes.TryGetValue( wallet.CardBrand, out var suffix ) ? suffix : currency;
        }

        /// <summary>
        /// The summary Rock's registration entry puts on a payment and a payment plan (Registration.GetSummary).
        /// </summary>
        private static string Summary( PlannedRegistration registration, int registrationId, RegistrationIds ids )
        {
            var instance = registration.Instance;
            var cost = registration.DiscountedCost;
            var registrants = string.Join( ", ", registration.Registrants.Select( r => $"{FullName( r.Person )} Cost/Fees:{Currency( r.DiscountedCost( registration.Discount ) )}" ) );
            return $"Event registration payment for {instance.Name} [ID:{ids.InstanceIds[instance]}] (Template: {instance.Template.Name} [ID:{ids.Templates[instance.Template.Key].Id}]).\r\n"
                + $"Registration By: {FullName( registration.Registrar )} Total Cost/Fees:{Currency( cost )}\r\nRegistrants: {registrants}";
        }

        private static string FullName( LoadedPerson person ) => $"{person.Cast.NickName ?? person.Cast.FirstName} {person.Cast.LastName}";

        private static string Currency( decimal amount ) => amount.ToString( "C", CultureInfo.GetCultureInfo( "en-US" ) );

        private static DataTable AttributeValueTable() => TableFactory.Create( "IsSystem:bool", "AttributeId:int", "EntityId:int", "Value:string", "IsPersistedValueDirty:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

        private static int DateKey( DateTime day ) => int.Parse( day.ToString( "yyyyMMdd", CultureInfo.InvariantCulture ), CultureInfo.InvariantCulture );

        private int NextId( string table ) => Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );
    }
}
