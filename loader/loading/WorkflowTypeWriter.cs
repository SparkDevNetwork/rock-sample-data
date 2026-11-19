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

using Microsoft.Data.SqlClient;

using RockSampleData.Loader.Database;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Writes a workflow type's rows the way Rock's workflow editor saves them: the type, its attributes, its activity types,
    /// its forms, and its action types with their settings. The flow and step workflow writers share it.
    /// </summary>
    internal sealed class WorkflowTypeWriter
    {
        private static readonly Guid WorkflowEntityTypeGuid = new Guid( "3540E9A7-FE30-43A9-8B0A-A372B63DFC93" );
        private static readonly Guid ActionTypeEntityTypeGuid = new Guid( "23E3273A-B137-48A3-9AFF-C8DC832DDCA6" );

        private readonly RockDatabase _db;
        private readonly string _foreignKey;

        public WorkflowTypeWriter( RockDatabase db, string foreignKey )
        {
            _db = db;
            _foreignKey = foreignKey;
            WorkflowEntityTypeId = db.RequireIdByGuid( "EntityType", WorkflowEntityTypeGuid );
            ActionTypeEntityTypeId = db.RequireIdByGuid( "EntityType", ActionTypeEntityTypeGuid );
        }

        /// <summary>
        /// Rock.Model.Workflow's entity type, which workflow attributes belong to.
        /// </summary>
        public int WorkflowEntityTypeId { get; }

        /// <summary>
        /// Rock.Model.WorkflowActionType's entity type, which action settings belong to.
        /// </summary>
        public int ActionTypeEntityTypeId { get; }

        /// <summary>
        /// Writes a persisted, active workflow type with no logging and Rock's defaults for everything else.
        /// </summary>
        public int InsertType( string name, string description, int categoryId, string workTerm, string iconCssClass, string prefix, Guid guid, DateTime created, int authorAliasId )
        {
            return Insert( @"
INSERT INTO [WorkflowType] ( [IsSystem], [IsActive], [Name], [Description], [CategoryId], [Order], [WorkTerm], [IsPersisted], [LoggingLevel], [IconCssClass], [WorkflowIdPrefix],
    [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( 0, 1, @Name, @Description, @CategoryId, 0, @WorkTerm, 1, 0, @Icon, @Prefix, @Guid, @Created, @Created, @Author, @Author, @ForeignKey )",
                P( "@Name", name ), P( "@Description", description ), P( "@CategoryId", categoryId ), P( "@WorkTerm", workTerm ), P( "@Icon", iconCssClass ),
                P( "@Prefix", prefix ), P( "@Guid", guid ), P( "@Created", created ), P( "@Author", authorAliasId ) );
        }

        /// <summary>
        /// Writes an attribute: a workflow attribute (qualified by WorkflowTypeId) or an action setting (by EntityTypeId).
        /// </summary>
        public int InsertAttribute( int fieldTypeId, int entityTypeId, string qualifierColumn, string qualifierValue, string key, string name, string description, int order, bool required,
            string defaultValue, Guid guid, DateTime created, bool isSystem = false )
        {
            return Insert( @"
INSERT INTO [Attribute] ( [IsSystem], [FieldTypeId], [EntityTypeId], [EntityTypeQualifierColumn], [EntityTypeQualifierValue], [Key], [Name], [Description], [Order], [IsGridColumn], [DefaultValue],
    [IsMultiValue], [IsRequired], [Guid], [CreatedDateTime], [ModifiedDateTime], [IsActive], [IsPublic], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @IsSystem, @FieldTypeId, @EntityTypeId, @Column, @Value, @Key, @Name, @Description, @Order, 0, @Default, 0, @Required, @Guid, @Created, @Created, 1, 0, @ForeignKey )",
                P( "@IsSystem", isSystem ), P( "@FieldTypeId", fieldTypeId ), P( "@EntityTypeId", entityTypeId ), P( "@Column", qualifierColumn ), P( "@Value", qualifierValue ), P( "@Key", key ),
                P( "@Name", name ), P( "@Description", ( object ) description ?? DBNull.Value ), P( "@Order", order ), P( "@Default", ( object ) defaultValue ?? string.Empty ), P( "@Required", required ),
                P( "@Guid", guid ), P( "@Created", created ) );
        }

        /// <summary>
        /// Writes a workflow attribute of the type.
        /// </summary>
        public int InsertWorkflowAttribute( int typeId, string fieldTypeClass, string key, string name, string description, int order, bool required, string defaultValue, Guid guid, DateTime created )
        {
            return InsertAttribute( FieldType( fieldTypeClass ), WorkflowEntityTypeId, "WorkflowTypeId", typeId.ToString( System.Globalization.CultureInfo.InvariantCulture ), key, name, description, order,
                required, defaultValue, guid, created );
        }

        /// <summary>
        /// Writes an active activity type that starts with the workflow.
        /// </summary>
        public int InsertActivityType( int typeId, string name, Guid guid, DateTime created, int authorAliasId )
        {
            return Insert( @"
INSERT INTO [WorkflowActivityType] ( [IsActive], [WorkflowTypeId], [Name], [IsActivatedWithWorkflow], [Order], [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( 1, @TypeId, @Name, 1, 0, @Guid, @Created, @Created, @Author, @Author, @ForeignKey )",
                P( "@TypeId", typeId ), P( "@Name", name ), P( "@Guid", guid ), P( "@Created", created ), P( "@Author", authorAliasId ) );
        }

        /// <summary>
        /// Writes a user entry form with no notification email. <paramref name="actions"/> is Rock's button list:
        /// label^button type^activity^response text, each ended with "|".
        /// </summary>
        public int InsertForm( string header, string footer, string actions, Guid guid, DateTime created, int authorAliasId )
        {
            return Insert( @"
INSERT INTO [WorkflowActionForm] ( [Header], [Footer], [Actions], [AllowNotes], [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @Header, @Footer, @Actions, 0, @Guid, @Created, @Created, @Author, @Author, @ForeignKey )",
                P( "@Header", header ), P( "@Footer", footer ), P( "@Actions", actions ), P( "@Guid", guid ), P( "@Created", created ), P( "@Author", authorAliasId ) );
        }

        /// <summary>
        /// Shows, or keeps hidden, one workflow attribute on a form. Rock keeps a row for every workflow attribute.
        /// </summary>
        public void InsertFormAttribute( int formId, int attributeId, int order, bool visible, bool readOnly, bool required, Guid guid, DateTime created, int authorAliasId )
        {
            _db.Execute( @"
INSERT INTO [WorkflowActionFormAttribute] ( [WorkflowActionFormId], [AttributeId], [Order], [IsVisible], [IsReadOnly], [IsRequired], [Guid], [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
VALUES ( @FormId, @AttributeId, @Order, @Visible, @ReadOnly, @Required, @Guid, @Created, @Created, @Author, @Author, @ForeignKey )",
                P( "@FormId", formId ), P( "@AttributeId", attributeId ), P( "@Order", order ), P( "@Visible", visible ), P( "@ReadOnly", readOnly ), P( "@Required", required ), P( "@Guid", guid ),
                P( "@Created", created ), P( "@Author", authorAliasId ), P( "@ForeignKey", _foreignKey ) );
        }

        /// <summary>
        /// Writes an action type that completes when it succeeds, and its settings as the action's attribute values.
        /// </summary>
        /// <param name="settingGuid">The Guid of each setting's attribute value, by setting key.</param>
        public int InsertActionType( int activityTypeId, string name, int order, int entityTypeId, bool completesActivity, int? formId, Guid guid, DateTime created, int authorAliasId,
            IEnumerable<(string Key, string Value)> settings, Func<string, Guid> settingGuid )
        {
            var actionTypeId = Insert( @"
INSERT INTO [WorkflowActionType] ( [ActivityTypeId], [Name], [Order], [EntityTypeId], [IsActionCompletedOnSuccess], [IsActivityCompletedOnSuccess], [WorkflowFormId], [Guid],
    [CreatedDateTime], [ModifiedDateTime], [CreatedByPersonAliasId], [ModifiedByPersonAliasId], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( @ActivityTypeId, @Name, @Order, @EntityTypeId, 1, @CompletesActivity, @FormId, @Guid, @Created, @Created, @Author, @Author, @ForeignKey )",
                P( "@ActivityTypeId", activityTypeId ), P( "@Name", name ), P( "@Order", order ), P( "@EntityTypeId", entityTypeId ), P( "@CompletesActivity", completesActivity ),
                P( "@FormId", ( object ) formId ?? DBNull.Value ), P( "@Guid", guid ), P( "@Created", created ), P( "@Author", authorAliasId ) );

            foreach ( var (key, value) in settings )
            {
                var attributeId = Convert.ToInt32( _db.Scalar( @"
SELECT [Id] FROM [Attribute] WHERE [EntityTypeId] = @EntityTypeId AND [EntityTypeQualifierColumn] = 'EntityTypeId' AND [EntityTypeQualifierValue] = @Component AND [Key] = @Key",
                    P( "@EntityTypeId", ActionTypeEntityTypeId ), P( "@Component", entityTypeId.ToString( System.Globalization.CultureInfo.InvariantCulture ) ), P( "@Key", key ) )
                    ?? throw new InvalidOperationException( $"Rock has no '{key}' setting for the workflow action {name}." ) );
                _db.Execute( @"
INSERT INTO [AttributeValue] ( [IsSystem], [AttributeId], [EntityId], [Value], [IsPersistedValueDirty], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( 0, @AttributeId, @EntityId, @Value, 1, @Guid, @ForeignKey, @Created, @Created )",
                    P( "@AttributeId", attributeId ), P( "@EntityId", actionTypeId ), P( "@Value", value ), P( "@Guid", settingGuid( key ) ), P( "@ForeignKey", _foreignKey ), P( "@Created", created ) );
            }

            return actionTypeId;
        }

        /// <summary>
        /// The Id of a Rock entity type by class name, such as a workflow action.
        /// </summary>
        public int EntityType( string className ) => Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [EntityType] WHERE [Name] = @Name", P( "@Name", className ) )
            ?? throw new InvalidOperationException( $"Rock has no entity type {className}." ) );

        /// <summary>
        /// The Id of a Rock field type by class name.
        /// </summary>
        public int FieldType( string className ) => Convert.ToInt32( _db.Scalar( "SELECT [Id] FROM [FieldType] WHERE [Class] = @Class", P( "@Class", className ) )
            ?? throw new InvalidOperationException( $"Rock has no field type {className}." ) );

        /// <summary>
        /// The next Id a bulk copy with explicit Ids starts from.
        /// </summary>
        public int NextId( string table ) => Convert.ToInt32( _db.Scalar( $"SELECT ISNULL( MAX( [Id] ), 0 ) + 1 FROM [{table}]" ) );

        /// <summary>
        /// Runs an INSERT that outputs the new Id, adding the ForeignKey when the statement names it.
        /// </summary>
        public int Insert( string sql, params SqlParameter[] parameters )
        {
            var all = parameters.Concat( sql.Contains( "@ForeignKey" ) ? new[] { P( "@ForeignKey", _foreignKey ) } : Array.Empty<SqlParameter>() ).ToArray();
            return Convert.ToInt32( _db.Scalar( sql, all ) );
        }
    }
}
