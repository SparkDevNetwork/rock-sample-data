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

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Writes what a church sets up before anyone registers: template categories, group types, templates with their
    /// forms, attributes, fees, discounts, and placements, and each planned instance with its linked group, placement
    /// groups, and linkage to its calendar occurrence (docs/registrations.md). The plan comes from <see cref="RegistrationPlanner"/>.
    /// </summary>
    internal sealed class RegistrationsLoader
    {
        private const string PlacementPurposeKey = "PLACEMENT";

        /// <summary>
        /// Rock's RegistrationFieldSource values.
        /// </summary>
        private const int RegistrationFieldSourcePerson = 0;
        private const int RegistrationFieldSourcePersonAttribute = 1;
        private const int RegistrationFieldSourceRegistrant = 4;

        /// <summary>
        /// Rock's RegistrationFeeType values.
        /// </summary>
        private const int FeeTypeSingle = 0;
        private const int FeeTypeMultiple = 1;

        /// <summary>
        /// Rock's RegistrationNotify value that emails the instance's contact about each registration.
        /// </summary>
        private const int NotifyRegistrationContact = 1;

        /// <summary>
        /// Days between payment reminders on a paid template.
        /// </summary>
        private const int PaymentReminderDays = 14;

        private const string RegistrationAttributeTitle = "Registration Information";

        private static readonly Guid CategoryEntityTypeGuid = new Guid( "1D68154E-EC76-44C8-9813-7736B27AECF9" );
        private static readonly Guid TemplateEntityTypeGuid = new Guid( "A01E3E99-A8AD-4C6C-BAAC-98795738BA70" );
        private static readonly Guid InstanceEntityTypeGuid = new Guid( "5CD9C0C8-C047-61A0-4E36-0FDB8496F066" );
        private static readonly Guid RegistrationEntityTypeGuid = new Guid( "D2F294C6-E161-4A56-85C7-CD74D535F61A" );
        private static readonly Guid RegistrantEntityTypeGuid = new Guid( "8A25E5CE-1B4F-4825-BCEA-216167836305" );
        private static readonly Guid GroupEntityTypeGuid = new Guid( "9BBFDA11-0D22-40D5-902F-60ADFBC88987" );
        private static readonly Guid TextFieldTypeGuid = new Guid( "9C204CD0-1233-41C5-818A-C5DA439445AA" );
        private static readonly Guid SingleSelectFieldTypeGuid = new Guid( "7525C4CB-EE6B-41D4-9B64-A08048D5A5C0" );

        /// <summary>
        /// Rock's Transaction Frequency values a payment plan may use.
        /// </summary>
        private static readonly Dictionary<PaymentPlanFrequency, Guid> FrequencyGuids = new Dictionary<PaymentPlanFrequency, Guid>
        {
            [PaymentPlanFrequency.Monthly] = new Guid( "1400753C-A0F9-4A45-8A1D-81C98450BD1F" ),
            [PaymentPlanFrequency.TwiceMonthly] = new Guid( "791C863D-2600-445B-98F8-3E5B66A3DEC4" )
        };

        /// <summary>
        /// A form field's visibility rules with none set, as Rock's model serializes them on save.
        /// </summary>
        private const string NoVisibilityRules = "{\"RuleList\":[],\"FilterExpressionType\":1}";

        private readonly RockDatabase _db;
        private readonly SampleDataSource _source;
        private readonly AnchorDates _dates;
        private readonly string _dataset;
        private readonly string _foreignKey;

        public RegistrationsLoader( RockDatabase db, SampleDataSource source, AnchorDates dates, string dataset, string foreignKey )
        {
            _db = db;
            _source = source;
            _dates = dates;
            _dataset = dataset;
            _foreignKey = foreignKey;
        }

        /// <summary>
        /// Plans and writes every registration. Run after the calendar, whose occurrences the linkages point at.
        /// Returns the plan, or null when the package has no registrations or no calendar.
        /// </summary>
        /// <param name="calendar">The calendar's planned events, from <see cref="CalendarLoader.Load"/>.</param>
        /// <param name="people">The loaded people: each instance's contact is a Cast person.</param>
        /// <param name="campusPlans">The dataset's campuses.</param>
        public RegistrationPlan Load( List<PlannedEvent> calendar, PeopleLoader people, IReadOnlyDictionary<string, CampusPlan> campusPlans )
        {
            var config = _source.Registrations;
            if ( config == null || calendar == null )
            {
                _db.Log.Write( config == null ? "  Registrations skipped (this package has no configuration/registrations.json)" : "  Registrations skipped (this package has no calendar to link them to)" );
                return null;
            }

            var campusNames = _source.Campuses.Campuses.Where( c => campusPlans.ContainsKey( c.Key ) ).ToDictionary( c => c.Key, c => c.Name );
            var plan = new RegistrationPlanner( config, _dataset, _dates.Anchor, campusNames ).Plan( calendar );
            var firstOpen = plan.Instances.Select( i => i.Opens ).DefaultIfEmpty( _dates.Anchor ).Min();

            var categoryIds = WriteCategories( config, Created( firstOpen ) );
            var groupTypes = WriteGroupTypes( config, Created( firstOpen ) );
            var templates = WriteTemplates( config, plan, categoryIds, groupTypes );
            var groupIds = WriteGroups( plan, groupTypes, campusPlans );
            var instanceIds = WriteInstances( config, plan, templates, people );
            WritePlacementGroupLinks( plan, instanceIds, groupIds, templates );
            WriteLinkages( plan, instanceIds, groupIds, campusPlans );

            var open = plan.Instances.Count( i => i.IsOpenOn( _dates.Anchor ) );
            _db.Log.Write( $"  Registrations: {config.Templates.Count} templates, {plan.Instances.Count} instances ({open} open today), {plan.Groups.Count} linked and placement groups" );
            return plan;
        }

        /// <summary>
        /// Adds the template categories, with Rock's registration admin role allowed to edit and administrate each.
        /// </summary>
        private Dictionary<string, int> WriteCategories( RegistrationsConfiguration config, DateTime created )
        {
            var categoryEntityTypeId = _db.RequireIdByGuid( "EntityType", CategoryEntityTypeGuid );
            var templateEntityTypeId = _db.RequireIdByGuid( "EntityType", TemplateEntityTypeGuid );
            var adminRoleId = _db.RequireIdByGuid( "Group", config.AdminRole );
            var nextId = NextId( "Category" );
            var ids = new Dictionary<string, int>( StringComparer.Ordinal );
            var categories = TableFactory.Create( "Id:int", "IsSystem:bool", "EntityTypeId:int", "EntityTypeQualifierColumn:string", "EntityTypeQualifierValue:string", "Name:string", "IconCssClass:string", "Order:int",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var auth = TableFactory.Create( "EntityTypeId:int", "EntityId:int", "Order:int", "Action:string", "AllowOrDeny:string", "SpecialRole:int", "GroupId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            var order = 0;
            foreach ( var category in config.Categories )
            {
                var id = nextId++;
                ids[category.Key] = id;
                categories.Rows.Add( id, false, templateEntityTypeId, string.Empty, string.Empty, category.Name, category.IconCssClass, order++,
                    SeededRandom.StableGuid( $"registration-category:{category.Key}" ), _foreignKey, created, created );
                foreach ( var action in new[] { "Edit", "Administrate" } )
                {
                    auth.Rows.Add( categoryEntityTypeId, id, 0, action, "A", 0, adminRoleId, SeededRandom.StableGuid( $"registration-category-auth:{category.Key}:{action}" ), _foreignKey, created, created );
                }
            }

            _db.BulkInsert( "Category", categories, keepIdentity: true );
            _db.BulkInsert( "Auth", auth, keepIdentity: false );
            return ids;
        }

        /// <summary>
        /// Adds each group type and its roles, unless Rock already has a type with its Guid. Returns each type by key.
        /// </summary>
        private Dictionary<string, GroupTypeInfo> WriteGroupTypes( RegistrationsConfiguration config, DateTime created )
        {
            var types = new Dictionary<string, GroupTypeInfo>( StringComparer.Ordinal );
            var nextTypeId = NextId( "GroupType" );
            var nextRoleId = NextId( "GroupTypeRole" );
            var groupTypes = TableFactory.Create( "Id:int", "IsSystem:bool", "Name:string", "Description:string", "GroupTerm:string", "GroupMemberTerm:string", "AllowMultipleLocations:bool", "ShowInGroupList:bool", "ShowInNavigation:bool",
                "IconCssClass:string", "TakesAttendance:bool", "AttendanceRule:int", "AttendancePrintTo:int", "Order:int", "LocationSelectionMode:int", "ScheduleConfirmationEmailOffsetDays:int", "ScheduleReminderEmailOffsetDays:int",
                "AdministratorTerm:string", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var roles = TableFactory.Create( "Id:int", "IsSystem:bool", "GroupTypeId:int", "Name:string", "Order:int", "IsLeader:bool", "CanView:bool", "CanManageMembers:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            foreach ( var type in config.GroupTypes )
            {
                var guid = type.Guid ?? SeededRandom.StableGuid( $"registration-group-type:{type.Key}" );
                var existingId = _db.IdByGuid( "GroupType", guid );
                if ( existingId != null )
                {
                    var defaultRoleId = Convert.ToInt32( _db.Scalar( "SELECT [DefaultGroupRoleId] FROM [GroupType] WHERE [Id] = @Id", P( "@Id", existingId.Value ) ) );
                    types[type.Key] = new GroupTypeInfo( existingId.Value, type.Roles.ToDictionary( r => r.Key, r => _db.IdByGuid( "GroupTypeRole", RoleGuid( type, r ) ) ?? defaultRoleId ), defaultRoleId );
                    continue;
                }

                var typeId = nextTypeId++;
                groupTypes.Rows.Add( typeId, false, type.Name, type.Description, type.GroupTerm, type.MemberTerm, false, true, true, type.IconCssClass, false, 0, 0, 0, 0, 4, 2, "Administrator",
                    guid, _foreignKey, created, created );

                var roleIds = new Dictionary<string, int>( StringComparer.Ordinal );
                var order = 0;
                foreach ( var role in type.Roles )
                {
                    var roleId = nextRoleId++;
                    roleIds[role.Key] = roleId;
                    roles.Rows.Add( roleId, false, typeId, role.Name, order++, role.IsLeader, role.IsLeader, role.IsLeader, RoleGuid( type, role ), _foreignKey, created, created );
                }

                var defaultRole = type.Roles.SingleOrDefault( r => r.IsDefault ) ?? throw new InvalidDataException( $"configuration/registrations.json: group type {type.Key} needs one default role." );
                types[type.Key] = new GroupTypeInfo( typeId, roleIds, roleIds[defaultRole.Key] );
            }

            _db.BulkInsert( "GroupType", groupTypes, keepIdentity: true );
            _db.BulkInsert( "GroupTypeRole", roles, keepIdentity: true );
            foreach ( DataRow row in groupTypes.Rows )
            {
                var type = types.Values.Single( t => t.Id == ( int ) row["Id"] );
                _db.Execute( "UPDATE [GroupType] SET [DefaultGroupRoleId] = @RoleId WHERE [Id] = @Id", P( "@RoleId", type.DefaultRoleId ), P( "@Id", type.Id ) );
            }

            return types;
        }

        private static Guid RoleGuid( RegistrationGroupTypeDefinition type, RegistrationGroupRoleDefinition role ) => SeededRandom.StableGuid( $"registration-group-role:{type.Key}:{role.Key}" );

        /*
            10/28/26 - CLAUDE

            Every template copies its emails and success text from Rock's own Baptism template, the way a template
            made from Rock's screen starts with Rock's defaults. The Lava stays Rock's, so it tracks Rock's wording.
            Eligibility goes in AdditionalSettingsJson under RegistrantEligibilitySettings, in the shape Rock's
            SetAdditionalSettings writes (Newtonsoft, properties in declaration order, enums as numbers).

            Reason: Templates read and send the way a church's own templates do.
        */

        /// <summary>
        /// Adds every template with its attributes, forms, fees, discounts, and placements. Returns each template by key.
        /// </summary>
        private Dictionary<string, TemplateInfo> WriteTemplates( RegistrationsConfiguration config, RegistrationPlan plan, Dictionary<string, int> categoryIds, Dictionary<string, GroupTypeInfo> groupTypes )
        {
            var templates = new Dictionary<string, TemplateInfo>( StringComparer.Ordinal );
            var nextId = NextId( "RegistrationTemplate" );
            foreach ( var template in config.Templates )
            {
                var opens = plan.Instances.Where( i => i.Template == template ).Select( i => i.Opens ).DefaultIfEmpty( _dates.Anchor ).Min();
                templates[template.Key] = new TemplateInfo( nextId++, template, Created( opens ) );
            }

            WriteTemplateRows( config, templates, categoryIds, groupTypes );
            var attributeIds = WriteAttributes( config, templates );
            WriteForms( templates, attributeIds );
            WriteFees( templates );
            WriteDiscounts( templates, plan );
            WritePlacements( templates, groupTypes );
            return templates;
        }

        private void WriteTemplateRows( RegistrationsConfiguration config, Dictionary<string, TemplateInfo> templates, Dictionary<string, int> categoryIds, Dictionary<string, GroupTypeInfo> groupTypes )
        {
            var rockTemplate = _db.Query( @"
SELECT [FeeTerm], [RegistrationTerm], [DiscountCodeTerm], [ConfirmationEmailTemplate], [ReminderEmailTemplate], [RequestEntryName], [SuccessTitle], [SuccessText],
    [ConfirmationFromName], [ConfirmationFromEmail], [ConfirmationSubject], [ReminderFromName], [ReminderFromEmail], [ReminderSubject],
    [PaymentReminderFromName], [PaymentReminderFromEmail], [PaymentReminderSubject], [PaymentReminderEmailTemplate],
    [WaitListTransitionFromName], [WaitListTransitionFromEmail], [WaitListTransitionSubject], [WaitListTransitionEmailTemplate]
FROM [RegistrationTemplate]
WHERE [Guid] = @Guid", P( "@Guid", config.EmailsFrom ) ).SingleOrDefault()
                ?? throw new InvalidOperationException( $"Rock's registration template {config.EmailsFrom}, whose emails every template copies, was not found." );
            object Rock( string column ) => rockTemplate[column] ?? DBNull.Value;

            var gatewayId = _db.RequireIdByGuid( "FinancialGateway", config.Gateway );
            var frequencyIds = FrequencyGuids.ToDictionary( f => f.Key, f => _db.RequireIdByGuid( "DefinedValue", f.Value ) );
            var rows = TableFactory.Create( "Id:int", "Name:string", "Description:string", "CategoryId:int", "GroupTypeId:int", "GroupMemberRoleId:int", "GroupMemberStatus:int", "FeeTerm:string", "RegistrantTerm:string", "RegistrationTerm:string",
                "DiscountCodeTerm:string", "ConfirmationEmailTemplate:string", "ReminderEmailTemplate:string", "Cost:decimal", "MinimumInitialPayment:decimal", "LoginRequired:bool", "RegistrantsSameFamily:int", "RequestEntryName:string",
                "SuccessTitle:string", "SuccessText:string", "AllowMultipleRegistrants:bool", "MaxRegistrants:int", "FinancialGatewayId:int", "IsActive:bool", "Notify:int", "ConfirmationFromName:string", "ConfirmationFromEmail:string",
                "ConfirmationSubject:string", "ReminderFromName:string", "ReminderFromEmail:string", "ReminderSubject:string", "AddPersonNote:bool", "AllowGroupPlacement:bool", "SetCostOnInstance:bool", "PaymentReminderFromName:string",
                "PaymentReminderFromEmail:string", "PaymentReminderSubject:string", "PaymentReminderEmailTemplate:string", "PaymentReminderTimeSpan:int", "AllowExternalRegistrationUpdates:bool", "BatchNamePrefix:string",
                "SignatureDocumentAction:int", "ShowCurrentFamilyMembers:bool", "WaitListEnabled:bool", "WaitListTransitionFromName:string", "WaitListTransitionFromEmail:string", "WaitListTransitionSubject:string",
                "WaitListTransitionEmailTemplate:string", "RegistrationInstructions:string", "RegistrarOption:int", "RegistrationAttributeTitleStart:string", "RegistrationAttributeTitleEnd:string", "IsRegistrationMeteringEnabled:bool",
                "ShowSmsOptIn:bool", "IsPaymentPlanAllowed:bool", "PaymentPlanFrequencyValueIds:string", "AdditionalSettingsJson:string", "AreDuplicateRegistrantsPrevented:bool", "IsFullPaymentOrPaymentPlanRequired:bool",
                "FullPaymentOrPaymentPlanRequiredMessage:string", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            foreach ( var info in templates.Values )
            {
                var template = info.Definition;
                var categoryId = categoryIds.TryGetValue( template.Category, out var category ) ? category
                    : throw new InvalidDataException( $"configuration/registrations.json: template {template.Key} has category {template.Category}, which 'categories' does not hold." );
                GroupTypeInfo groupType = null;
                if ( template.GroupType != null && !groupTypes.TryGetValue( template.GroupType, out groupType ) )
                {
                    throw new InvalidDataException( $"configuration/registrations.json: template {template.Key} has group type {template.GroupType}, which 'groupTypes' does not hold." );
                }

                var roleId = groupType == null ? ( object ) DBNull.Value : groupType.RoleIds.TryGetValue( template.GroupMemberRole ?? string.Empty, out var role ) ? role : groupType.DefaultRoleId;
                var isPaymentPlanAllowed = template.PaymentPlan.Count > 0;
                var frequencies = string.Join( ",", template.PaymentPlan.Select( f => frequencyIds[f].ToString( CultureInfo.InvariantCulture ) ) );

                rows.Rows.Add( info.Id, template.Name, template.Description, categoryId, ( object ) groupType?.Id ?? DBNull.Value, roleId, ( int ) template.GroupMemberStatus, Rock( "FeeTerm" ), template.RegistrantTerm, Rock( "RegistrationTerm" ),
                    Rock( "DiscountCodeTerm" ), Rock( "ConfirmationEmailTemplate" ), Rock( "ReminderEmailTemplate" ), template.Cost, ( object ) template.MinimumInitialPayment ?? DBNull.Value, false, ( int ) template.RegistrantsSameFamily,
                    Rock( "RequestEntryName" ), Rock( "SuccessTitle" ), Rock( "SuccessText" ), template.MaxRegistrants > 1, template.MaxRegistrants, template.IsPaid ? gatewayId : DBNull.Value, true, NotifyRegistrationContact,
                    Rock( "ConfirmationFromName" ), Rock( "ConfirmationFromEmail" ), Rock( "ConfirmationSubject" ), Rock( "ReminderFromName" ), Rock( "ReminderFromEmail" ), Rock( "ReminderSubject" ), true, template.Placements.Count > 0,
                    template.SetCostOnInstance, Rock( "PaymentReminderFromName" ), Rock( "PaymentReminderFromEmail" ), Rock( "PaymentReminderSubject" ), Rock( "PaymentReminderEmailTemplate" ), template.IsPaid ? PaymentReminderDays : DBNull.Value, true,
                    template.IsPaid ? config.BatchPrefix : DBNull.Value, 0, template.ShowCurrentFamilyMembers, template.WaitList, Rock( "WaitListTransitionFromName" ), Rock( "WaitListTransitionFromEmail" ), Rock( "WaitListTransitionSubject" ),
                    Rock( "WaitListTransitionEmailTemplate" ), ( object ) template.Instructions ?? DBNull.Value, ( int ) template.RegistrarOption, RegistrationAttributeTitle, RegistrationAttributeTitle, false,
                    false, isPaymentPlanAllowed, isPaymentPlanAllowed ? frequencies : DBNull.Value, ( object ) EligibilityJson( template.Eligibility ) ?? DBNull.Value, false, template.FullPaymentOrPlanRequired != null,
                    ( object ) template.FullPaymentOrPlanRequired ?? DBNull.Value, SeededRandom.StableGuid( $"registration-template:{template.Key}" ), _foreignKey, info.Created, info.Created );
            }

            _db.BulkInsert( "RegistrationTemplate", rows, keepIdentity: true );
        }

        /// <summary>
        /// Adds each template's registrant attributes and the registration attributes every template asks. Returns
        /// attribute Ids by template key and registrant attribute key.
        /// </summary>
        private Dictionary<(string Template, string Key), int> WriteAttributes( RegistrationsConfiguration config, Dictionary<string, TemplateInfo> templates )
        {
            var registrantEntityTypeId = _db.RequireIdByGuid( "EntityType", RegistrantEntityTypeGuid );
            var registrationEntityTypeId = _db.RequireIdByGuid( "EntityType", RegistrationEntityTypeGuid );
            var fieldTypeIds = new Dictionary<RegistrationAttributeFieldType, int>
            {
                [RegistrationAttributeFieldType.Text] = _db.RequireIdByGuid( "FieldType", TextFieldTypeGuid ),
                [RegistrationAttributeFieldType.SingleSelect] = _db.RequireIdByGuid( "FieldType", SingleSelectFieldTypeGuid )
            };
            var nextId = NextId( "Attribute" );
            var ids = new Dictionary<(string Template, string Key), int>();
            var attributes = TableFactory.Create( "Id:int", "IsSystem:bool", "FieldTypeId:int", "EntityTypeId:int", "EntityTypeQualifierColumn:string", "EntityTypeQualifierValue:string", "Key:string", "Name:string", "Description:string",
                "Order:int", "IsGridColumn:bool", "DefaultValue:string", "IsMultiValue:bool", "IsRequired:bool", "AllowSearch:bool", "IsIndexEnabled:bool", "IsAnalytic:bool", "IsAnalyticHistory:bool", "IsActive:bool", "EnableHistory:bool",
                "ShowOnBulk:bool", "IsPublic:bool", "IsDefaultPersistedValueDirty:bool", "IsSuppressHistoryLogging:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var qualifiers = TableFactory.Create( "IsSystem:bool", "AttributeId:int", "Key:string", "Value:string", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            void Add( TemplateInfo info, RegistrationAttributeDefinition attribute, int entityTypeId, string kind, bool isRequired, int order )
            {
                var id = nextId++;
                var guid = SeededRandom.StableGuid( $"{kind}-attribute:{info.Definition.Key}:{attribute.Key}" );
                attributes.Rows.Add( id, false, fieldTypeIds[attribute.FieldType], entityTypeId, "RegistrationTemplateId", info.Id.ToString( CultureInfo.InvariantCulture ), attribute.Key, attribute.Name, attribute.Description ?? string.Empty,
                    order, attribute.IsGridColumn, string.Empty, false, isRequired, false, false, false, false, true, false, false, false, true, false, guid, _foreignKey, info.Created, info.Created );
                if ( attribute.FieldType == RegistrationAttributeFieldType.SingleSelect )
                {
                    qualifiers.Rows.Add( false, id, "values", string.Join( ",", attribute.Values ), SeededRandom.StableGuid( $"{guid}:values" ), _foreignKey, info.Created, info.Created );
                    qualifiers.Rows.Add( false, id, "fieldtype", "ddl", SeededRandom.StableGuid( $"{guid}:fieldtype" ), _foreignKey, info.Created, info.Created );
                }

                if ( kind == "registrant" )
                {
                    ids[(info.Definition.Key, attribute.Key)] = id;
                }
            }

            foreach ( var info in templates.Values )
            {
                var fields = info.Definition.Forms.SelectMany( f => f.Fields ).Where( f => f.Attribute != null ).ToList();
                var order = 0;
                foreach ( var attribute in info.Definition.Attributes )
                {
                    Add( info, attribute, registrantEntityTypeId, "registrant", fields.Any( f => f.Attribute == attribute.Key && f.Required ), order++ );
                }

                order = 0;
                foreach ( var attribute in config.RegistrationAttributes )
                {
                    Add( info, attribute, registrationEntityTypeId, "registration", false, order++ );
                }
            }

            _db.BulkInsert( "Attribute", attributes, keepIdentity: true );
            _db.BulkInsert( "AttributeQualifier", qualifiers, keepIdentity: false );
            return ids;
        }

        /// <summary>
        /// Adds each template's forms and fields. First and last name sit side by side, as Rock's template screen sets them.
        /// </summary>
        private void WriteForms( Dictionary<string, TemplateInfo> templates, Dictionary<(string Template, string Key), int> attributeIds )
        {
            var nextFormId = NextId( "RegistrationTemplateForm" );
            var personAttributeIds = new Dictionary<Guid, int>();
            var forms = TableFactory.Create( "Id:int", "Name:string", "RegistrationTemplateId:int", "Order:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var fields = TableFactory.Create( "RegistrationTemplateFormId:int", "FieldSource:int", "PersonFieldType:int", "AttributeId:int", "IsSharedValue:bool", "ShowCurrentValue:bool", "PreText:string", "PostText:string",
                "IsGridField:bool", "IsRequired:bool", "Order:int", "IsInternal:bool", "ShowOnWaitlist:bool", "FieldVisibilityRulesJSON:string", "IsLockedIfValuesExist:bool", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            foreach ( var info in templates.Values )
            {
                var template = info.Definition;
                for ( var formOrder = 0; formOrder < template.Forms.Count; formOrder++ )
                {
                    var form = template.Forms[formOrder];
                    var formId = nextFormId++;
                    forms.Rows.Add( formId, form.Name, info.Id, formOrder, SeededRandom.StableGuid( $"registration-form:{template.Key}:{formOrder}" ), _foreignKey, info.Created, info.Created );
                    for ( var order = 0; order < form.Fields.Count; order++ )
                    {
                        var field = form.Fields[order];
                        var (source, attributeId, isGrid) = FieldSource( template, field, attributeIds, personAttributeIds );
                        var (preText, postText) = field.Person switch
                        {
                            RegistrationPersonField.FirstName => ("<div class='row'><div class='col-md-6'>", "    </div>"),
                            RegistrationPersonField.LastName => ("    <div class='col-md-6'>", "    </div></div>"),
                            _ => ((string) null, (string) null)
                        };
                        var isPersonField = source == RegistrationFieldSourcePerson;
                        fields.Rows.Add( formId, source, ( int ) ( field.Person ?? RegistrationPersonField.FirstName ), ( object ) attributeId ?? DBNull.Value, false, source != RegistrationFieldSourceRegistrant,
                            ( object ) preText ?? DBNull.Value, ( object ) postText ?? DBNull.Value, isGrid, field.Required, order, false, isPersonField, NoVisibilityRules, false,
                            SeededRandom.StableGuid( $"registration-form-field:{template.Key}:{formOrder}:{order}" ), _foreignKey, info.Created, info.Created );
                    }
                }
            }

            _db.BulkInsert( "RegistrationTemplateForm", forms, keepIdentity: true );
            _db.BulkInsert( "RegistrationTemplateFormField", fields, keepIdentity: false );
        }

        /// <summary>
        /// A field's source in Rock's RegistrationFieldSource terms, its attribute, and whether it shows in the registrant grid.
        /// </summary>
        private (int Source, int? AttributeId, bool IsGrid) FieldSource( RegistrationTemplateDefinition template, RegistrationFieldDefinition field, Dictionary<(string Template, string Key), int> attributeIds, Dictionary<Guid, int> personAttributeIds )
        {
            if ( field.Person is { } person )
            {
                return (RegistrationFieldSourcePerson, null, person is RegistrationPersonField.FirstName or RegistrationPersonField.LastName);
            }

            if ( field.PersonAttribute is { } personAttribute )
            {
                if ( !personAttributeIds.TryGetValue( personAttribute, out var id ) )
                {
                    id = personAttributeIds[personAttribute] = _db.RequireIdByGuid( "Attribute", personAttribute );
                }

                return (RegistrationFieldSourcePersonAttribute, id, false);
            }

            var attribute = template.Attributes.FirstOrDefault( a => a.Key == field.Attribute )
                ?? throw new InvalidDataException( $"configuration/registrations.json: template {template.Key} has a field for attribute {field.Attribute}, which its 'attributes' does not hold." );
            return (RegistrationFieldSourceRegistrant, attributeIds[(template.Key, attribute.Key)], attribute.IsGridColumn);
        }

        /// <summary>
        /// Adds each template's fees and their items. A fee with more than one item is a multiple-choice fee.
        /// </summary>
        private void WriteFees( Dictionary<string, TemplateInfo> templates )
        {
            var nextFeeId = NextId( "RegistrationTemplateFee" );
            var fees = TableFactory.Create( "Id:int", "Name:string", "RegistrationTemplateId:int", "FeeType:int", "DiscountApplies:bool", "AllowMultiple:bool", "Order:int", "IsActive:bool", "IsRequired:bool", "HideWhenNoneRemaining:bool",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            var items = TableFactory.Create( "RegistrationTemplateFeeId:int", "Order:int", "IsActive:bool", "Name:string", "Cost:decimal", "MaximumUsageCount:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            foreach ( var info in templates.Values )
            {
                var template = info.Definition;
                for ( var order = 0; order < template.Fees.Count; order++ )
                {
                    var fee = template.Fees[order];
                    var feeId = nextFeeId++;
                    fees.Rows.Add( feeId, fee.Name, info.Id, fee.Items.Count > 1 ? FeeTypeMultiple : FeeTypeSingle, fee.DiscountApplies, fee.AllowMultiple, order, true, fee.Required, fee.HideWhenNoneRemaining,
                        SeededRandom.StableGuid( $"registration-fee:{template.Key}:{fee.Key}" ), _foreignKey, info.Created, info.Created );
                    for ( var itemOrder = 0; itemOrder < fee.Items.Count; itemOrder++ )
                    {
                        var item = fee.Items[itemOrder];
                        items.Rows.Add( feeId, itemOrder, true, item.Name, item.Cost, ( object ) item.MaximumUsageCount ?? DBNull.Value, SeededRandom.StableGuid( $"registration-fee-item:{template.Key}:{fee.Key}:{itemOrder}" ),
                            _foreignKey, info.Created, info.Created );
                    }
                }
            }

            _db.BulkInsert( "RegistrationTemplateFee", fees, keepIdentity: true );
            _db.BulkInsert( "RegistrationTemplateFeeItem", items, keepIdentity: false );
        }

        /// <summary>
        /// Adds each template's discounts. An early bird ends on its day before the template's latest event.
        /// </summary>
        private void WriteDiscounts( Dictionary<string, TemplateInfo> templates, RegistrationPlan plan )
        {
            var discounts = TableFactory.Create( "Code:string", "RegistrationTemplateId:int", "DiscountPercentage:decimal", "DiscountAmount:decimal", "Order:int", "MaxUsage:int", "MinRegistrants:int", "EndDate:date", "AutoApplyDiscount:bool",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            foreach ( var info in templates.Values )
            {
                var template = info.Definition;
                for ( var order = 0; order < template.Discounts.Count; order++ )
                {
                    var discount = template.Discounts[order];
                    var endDate = plan.DiscountEnds.TryGetValue( (template.Key, discount.Key), out var ends ) ? ( object ) ends : DBNull.Value;

                    // Rock keeps a percent as a fraction: 10% is 0.1.
                    discounts.Rows.Add( discount.Code, info.Id, discount.Percent / 100, discount.Amount, order, ( object ) discount.MaxUsage ?? DBNull.Value, ( object ) discount.MinRegistrants ?? DBNull.Value, endDate, discount.AutoApply,
                        SeededRandom.StableGuid( $"registration-discount:{template.Key}:{discount.Key}" ), _foreignKey, info.Created, info.Created );
                }
            }

            _db.BulkInsert( "RegistrationTemplateDiscount", discounts, keepIdentity: false );
        }

        /// <summary>
        /// Adds each template's placements, and records their Ids for the placement group links.
        /// </summary>
        private void WritePlacements( Dictionary<string, TemplateInfo> templates, Dictionary<string, GroupTypeInfo> groupTypes )
        {
            var nextId = NextId( "RegistrationTemplatePlacement" );
            var placements = TableFactory.Create( "Id:int", "Name:string", "RegistrationTemplateId:int", "GroupTypeId:int", "Order:int", "IconCssClass:string", "AllowMultiplePlacements:bool", "IsInternal:bool",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            foreach ( var info in templates.Values )
            {
                var template = info.Definition;
                for ( var order = 0; order < template.Placements.Count; order++ )
                {
                    var placement = template.Placements[order];
                    var groupType = groupTypes.TryGetValue( placement.GroupType, out var type ) ? type
                        : throw new InvalidDataException( $"configuration/registrations.json: template {template.Key} placement {placement.Key} has group type {placement.GroupType}, which 'groupTypes' does not hold." );
                    var id = nextId++;
                    info.PlacementIds[placement.Key] = id;
                    placements.Rows.Add( id, placement.Name, info.Id, groupType.Id, order, ( object ) placement.IconCssClass ?? DBNull.Value, placement.AllowMultiple, false,
                        SeededRandom.StableGuid( $"registration-placement:{template.Key}:{placement.Key}" ), _foreignKey, info.Created, info.Created );
                }
            }

            _db.BulkInsert( "RegistrationTemplatePlacement", placements, keepIdentity: true );
        }

        internal static string EligibilityJson( RegistrationEligibility eligibility )
        {
            if ( eligibility == null )
            {
                return null;
            }

            // Rock's grade offset counts years to graduation: 0 is a senior, so the youngest grade has the largest offset.
            string Number( decimal? value ) => value == null ? "null" : value.Value.ToString( value.Value == decimal.Truncate( value.Value ) ? "0.0" : "0.0###", CultureInfo.InvariantCulture );
            string Integer( int? value ) => value?.ToString( CultureInfo.InvariantCulture ) ?? "null";
            var grades = eligibility.Grades;
            var ages = eligibility.Ages;
            return "{\"RegistrantEligibilitySettings\":{"
                + $"\"MinimumAge\":{Number( ages?[0] )},\"MaximumAge\":{Number( ages?[1] )},\"AgeClassification\":null,"
                + $"\"MaximumGradeOffset\":{Integer( grades == null ? null : 12 - grades[0] )},\"MinimumGradeOffset\":{Integer( grades == null ? null : 12 - grades[1] )},"
                + $"\"Gender\":{Integer( ( int? ) eligibility.Gender )},\"EligibilityDataViewGuid\":null}}}}";
        }

        /// <summary>
        /// Adds every linked group and placement group. A group belongs to its instance's campus, when it has one.
        /// </summary>
        private Dictionary<string, int> WriteGroups( RegistrationPlan plan, Dictionary<string, GroupTypeInfo> groupTypes, IReadOnlyDictionary<string, CampusPlan> campusPlans )
        {
            var nextId = NextId( "Group" );
            var ids = new Dictionary<string, int>( StringComparer.Ordinal );
            var groups = TableFactory.Create( "Id:int", "IsSystem:bool", "GroupTypeId:int", "CampusId:int", "Name:string", "IsSecurityRole:bool", "IsActive:bool", "IsPublic:bool", "Order:int",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            foreach ( var group in plan.Groups.Values )
            {
                var typeId = groupTypes.TryGetValue( group.GroupType, out var type ) ? type.Id
                    : throw new InvalidDataException( $"configuration/registrations.json: group {group.Key} has group type {group.GroupType}, which 'groupTypes' does not hold." );
                var id = nextId++;
                ids[group.Key] = id;
                var created = Created( group.FirstInstance.Opens );
                var order = group.FirstInstance.PlacementGroups.FirstOrDefault( p => p.Group == group )?.Order ?? 0;
                groups.Rows.Add( id, false, typeId, group.CampusKey == null ? DBNull.Value : campusPlans[group.CampusKey].CampusId, group.Name, false, true, true, order,
                    group.Guid, _foreignKey, created, created );
            }

            _db.BulkInsert( "Group", groups, keepIdentity: true );
            return ids;
        }

        /// <summary>
        /// Adds every instance with its dates, limits, account, contact, and reminder.
        /// </summary>
        private Dictionary<PlannedRegistrationInstance, int> WriteInstances( RegistrationsConfiguration config, RegistrationPlan plan, Dictionary<string, TemplateInfo> templates, PeopleLoader people )
        {
            var account = _source.Accounts.Accounts.FirstOrDefault( a => a.Key == config.Account )
                ?? throw new InvalidDataException( $"configuration/registrations.json: account {config.Account} is not in configuration/accounts.json." );
            var accountId = _db.RequireIdByGuid( "FinancialAccount", account.Guid );
            var nextId = NextId( "RegistrationInstance" );
            var ids = new Dictionary<PlannedRegistrationInstance, int>();
            var rows = TableFactory.Create( "Id:int", "Name:string", "RegistrationTemplateId:int", "StartDateTime:date", "EndDateTime:date", "Details:string", "MaxAttendees:int", "AccountId:int", "IsActive:bool",
                "ContactEmail:string", "ContactPhone:string", "ContactPersonAliasId:int", "SendReminderDateTime:date", "ReminderSent:bool", "Cost:decimal", "MinimumInitialPayment:decimal", "RegistrationInstructions:string",
                "PaymentDeadlineDate:date", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );

            foreach ( var instance in plan.Instances )
            {
                var rule = instance.Rule;
                var contact = _source.Calendar.Contacts.TryGetValue( rule.Contact ?? string.Empty, out var found ) ? found
                    : throw new InvalidDataException( $"configuration/registrations.json: instance {rule.Key} has contact {rule.Contact}, which configuration/calendar.json's 'contacts' does not hold." );
                var person = people.People.TryGetValue( contact.Person, out var loaded ) ? loaded
                    : throw new InvalidDataException( $"configuration/calendar.json: contact {rule.Contact} is person {contact.Person}, who is not in the Cast." );
                var isCostOnInstance = instance.Template.SetCostOnInstance;
                var created = Created( instance.Opens );
                var id = nextId++;
                ids[instance] = id;

                // The last day of registration runs to its end, as a person picks 11:59 PM on Rock's screen.
                rows.Rows.Add( id, instance.Name, templates[instance.Template.Key].Id, instance.Opens, instance.Closes.AddDays( 1 ).AddMinutes( -1 ), ( object ) rule.Details ?? DBNull.Value,
                    ( object ) instance.MaxAttendees ?? DBNull.Value, accountId, true, contact.Email, contact.Phone, person.AliasId,
                    ( object ) instance.SendReminder ?? DBNull.Value, instance.SendReminder < _dates.Anchor, isCostOnInstance ? rule.Cost ?? 0 : DBNull.Value,
                    isCostOnInstance ? ( object ) rule.MinimumInitialPayment ?? DBNull.Value : DBNull.Value, ( object ) rule.Instructions ?? DBNull.Value,
                    ( object ) instance.PaymentDeadline ?? DBNull.Value, instance.Guid, _foreignKey, created, created );
            }

            _db.BulkInsert( "RegistrationInstance", rows, keepIdentity: true );
            return ids;
        }

        /// <summary>
        /// Links each instance to its placement groups, the way Rock's placement screen does: a RelatedEntity from the
        /// instance to the group, qualified by the template placement's Id. A group two instances share is linked to both.
        /// </summary>
        private void WritePlacementGroupLinks( RegistrationPlan plan, Dictionary<PlannedRegistrationInstance, int> instanceIds, Dictionary<string, int> groupIds, Dictionary<string, TemplateInfo> templates )
        {
            var instanceEntityTypeId = _db.RequireIdByGuid( "EntityType", InstanceEntityTypeGuid );
            var groupEntityTypeId = _db.RequireIdByGuid( "EntityType", GroupEntityTypeGuid );
            var rows = TableFactory.Create( "SourceEntityTypeId:int", "SourceEntityId:int", "TargetEntityTypeId:int", "TargetEntityId:int", "PurposeKey:string", "IsSystem:bool", "Order:int", "QualifierValue:string",
                "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            foreach ( var instance in plan.Instances )
            {
                var created = Created( instance.Opens );
                foreach ( var placement in instance.PlacementGroups )
                {
                    var placementId = templates[instance.Template.Key].PlacementIds[placement.Placement.Key];
                    rows.Rows.Add( instanceEntityTypeId, instanceIds[instance], groupEntityTypeId, groupIds[placement.Group.Key], PlacementPurposeKey, false, 0, placementId.ToString( CultureInfo.InvariantCulture ),
                        SeededRandom.StableGuid( $"registration-placement-group:{instance.Guid}:{placement.Group.Key}" ), _foreignKey, created, created );
                }
            }

            _db.BulkInsert( "RelatedEntity", rows, keepIdentity: false );
        }

        /// <summary>
        /// Links each instance to its calendar occurrence and linked group, with the public name and URL slug the event page uses.
        /// </summary>
        private void WriteLinkages( RegistrationPlan plan, Dictionary<PlannedRegistrationInstance, int> instanceIds, Dictionary<string, int> groupIds, IReadOnlyDictionary<string, CampusPlan> campusPlans )
        {
            var occurrenceIds = _db.Query( "SELECT [Id], [Guid] FROM [EventItemOccurrence] WHERE [ForeignKey] = @ForeignKey", P( "@ForeignKey", _foreignKey ) )
                .ToDictionary( r => ( Guid ) r["Guid"], r => Convert.ToInt32( r["Id"] ) );
            var rows = TableFactory.Create( "EventItemOccurrenceId:int", "RegistrationInstanceId:int", "GroupId:int", "PublicName:string", "UrlSlug:string", "CampusId:int", "Guid:guid", "ForeignKey:string", "CreatedDateTime:date", "ModifiedDateTime:date" );
            foreach ( var instance in plan.Instances )
            {
                var occurrenceId = occurrenceIds.TryGetValue( instance.Occurrence.Guid, out var found ) ? found
                    : throw new InvalidOperationException( $"Registration {instance.Name} links to calendar occurrence {instance.Occurrence.Key}, which the calendar did not write." );
                var created = Created( instance.Opens );
                rows.Rows.Add( occurrenceId, instanceIds[instance], instance.LinkedGroup == null ? DBNull.Value : groupIds[instance.LinkedGroup.Key], instance.PublicName, instance.Slug,
                    instance.CampusKey == null ? DBNull.Value : campusPlans[instance.CampusKey].CampusId, instance.LinkageGuid, _foreignKey, created, created );
            }

            _db.BulkInsert( "EventItemOccurrenceGroupMap", rows, keepIdentity: false );
        }

        /// <summary>
        /// When a row was added: two weeks before its registration opened, and never after the install day began.
        /// </summary>
        private DateTime Created( DateTime opens )
        {
            var created = opens.Date.AddDays( -14 ).AddHours( 10 );
            return created < _dates.Anchor ? created : _dates.Anchor;
        }

        private int NextId( string table ) => Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );

        /// <summary>
        /// A group type's Id, role Ids by key, and default role.
        /// </summary>
        private sealed record GroupTypeInfo( int Id, Dictionary<string, int> RoleIds, int DefaultRoleId );

        /// <summary>
        /// A template's Id and its placement Ids by key.

        /// <summary>
        /// A template's Id, definition, audit date, and placement Ids by key.
        /// </summary>
        private sealed class TemplateInfo
        {
            public TemplateInfo( int id, RegistrationTemplateDefinition definition, DateTime created )
            {
                Id = id;
                Definition = definition;
                Created = created;
            }

            public int Id { get; }

            public RegistrationTemplateDefinition Definition { get; }

            public DateTime Created { get; }

            public Dictionary<string, int> PlacementIds { get; } = new Dictionary<string, int>( StringComparer.Ordinal );
        }
    }
}
