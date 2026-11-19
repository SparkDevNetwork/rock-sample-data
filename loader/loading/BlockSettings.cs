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

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /// <summary>
    /// Saves block settings the way Rock's block settings dialog does: one attribute value per setting, on the block.
    /// </summary>
    internal static class BlockSettings
    {
        /// <summary>
        /// Saves setting values on one block, by the setting keys its block type has. Throws when the block type has no
        /// setting with a key.
        /// </summary>
        /// <param name="blockName">The block's name in Rock, for the error.</param>
        public static void Set( RockDatabase db, object blockId, object blockTypeId, string blockName, IReadOnlyDictionary<string, string> values, string foreignKey )
        {
            foreach ( var value in values )
            {
                var attributeId = db.Scalar( @"
SELECT TOP 1 [a].[Id]
FROM [Attribute] AS [a]
INNER JOIN [EntityType] AS [e] ON [e].[Id] = [a].[EntityTypeId]
WHERE [e].[Name] = 'Rock.Model.Block' AND [a].[Key] = @Key
    AND [a].[EntityTypeQualifierColumn] = 'BlockTypeId' AND [a].[EntityTypeQualifierValue] = @BlockTypeId",
                    P( "@Key", value.Key ), P( "@BlockTypeId", blockTypeId.ToString() ) )
                    ?? throw new InvalidOperationException( $"Rock's {blockName} block has no '{value.Key}' setting." );

                db.Execute( @"
IF NOT EXISTS ( SELECT 1 FROM [AttributeValue] WHERE [AttributeId] = @AttributeId AND [EntityId] = @EntityId )
    INSERT INTO [AttributeValue] ( [IsSystem], [AttributeId], [EntityId], [Value], [IsPersistedValueDirty], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
    VALUES ( 0, @AttributeId, @EntityId, @Value, 1, NEWID(), @ForeignKey, GETDATE(), GETDATE() )
ELSE
    UPDATE [AttributeValue] SET [Value] = @Value, [ValueAsNumeric] = @ValueAsNumeric, [ValueAsDateTime] = @ValueAsDateTime, [ValueAsBoolean] = @ValueAsBoolean, [IsPersistedValueDirty] = 1 WHERE [AttributeId] = @AttributeId AND [EntityId] = @EntityId",
                    new[] { P( "@AttributeId", attributeId ), P( "@EntityId", blockId ), P( "@Value", value.Value ), P( "@ForeignKey", foreignKey ) }.Concat( AttributeValueHook.Parameters( value.Value, DateTime.Today.Year ) ).ToArray() );
            }
        }
    }
}
