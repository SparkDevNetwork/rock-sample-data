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

using RockSampleData.Loader.Generation;
using RockSampleData.Loader.Model;

using static RockSampleData.Loader.Database.RockDatabase;

namespace RockSampleData.Loader.Loading
{
    /*
        11/16/26 - CLAUDE

        Rock ships no block that shows adaptive messages; only the adaptivemessage Lava command does, on a block that
        enables it. So the church's messages show on the External Website homepage in an HTML Content block, added the
        way an admin adds one in Rock: the block in a zone, its Enabled Lava Commands setting, and its content as one
        approved version with no context value, as Rock's HTML editor saves it. The block type keeps its Guid in Rock 20
        and Rock 21. The cache duration stays at Rock's default of none, because each person sees their own messages.

        Reason: Issue 6 question 1. Jon decides where messages show, so this lives in its own file and commit.
    */

    internal sealed partial class AdaptiveMessagesLoader
    {
        /// <summary>
        /// Rock's HTML Content block type, the same in Rock 20 and Rock 21.
        /// </summary>
        private static readonly Guid HtmlContentBlockTypeGuid = new Guid( "19B61D65-37E3-459F-A44F-DEF0089118A3" );

        /// <summary>
        /// Adds the HTML Content block that shows the church's messages, with the AdaptiveMessage Lava command enabled.
        /// </summary>
        private void AddDisplay( AdaptiveMessageDisplay display, int categoryId, DateTime now )
        {
            var blockTypeId = _db.RequireIdByGuid( "BlockType", HtmlContentBlockTypeGuid );
            var blockId = Convert.ToInt32( _db.Scalar( @"
INSERT INTO [Block] ( [IsSystem], [PageId], [BlockTypeId], [Zone], [Order], [Name], [OutputCacheDuration], [Guid], [CreatedDateTime], [ModifiedDateTime], [ForeignKey] )
OUTPUT INSERTED.[Id]
VALUES ( 0, @PageId, @BlockTypeId, @Zone, @Order, @Name, 0, @Guid, @Now, @Now, @ForeignKey )",
                P( "@PageId", _db.RequireIdByGuid( "Page", display.Page ) ), P( "@BlockTypeId", blockTypeId ), P( "@Zone", display.Zone ), P( "@Order", display.Order ),
                P( "@Name", display.Name ), P( "@Guid", display.BlockGuid ), P( "@Now", now ), P( "@ForeignKey", _foreignKey ) ) );

            var commandsId = _db.Scalar( @"
SELECT TOP 1 [Id] FROM [Attribute]
WHERE [EntityTypeId] = ( SELECT [Id] FROM [EntityType] WHERE [Name] = 'Rock.Model.Block' )
    AND [EntityTypeQualifierColumn] = 'BlockTypeId' AND [EntityTypeQualifierValue] = @BlockTypeId AND [Key] = 'EnabledLavaCommands'",
                P( "@BlockTypeId", blockTypeId.ToString() ) )
                ?? throw new InvalidDataException( "Rock's HTML Content block type has no Enabled Lava Commands setting." );
            _db.Execute( @"
INSERT INTO [AttributeValue] ( [IsSystem], [AttributeId], [EntityId], [Value], [IsPersistedValueDirty], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( 0, @AttributeId, @BlockId, 'AdaptiveMessage', 1, @Guid, @ForeignKey, @Now, @Now )",
                P( "@AttributeId", commandsId ), P( "@BlockId", blockId ), P( "@Guid", SeededRandom.StableGuid( $"adaptive-message-block-commands:{display.BlockGuid}" ) ),
                P( "@ForeignKey", _foreignKey ), P( "@Now", now ) );

            var lava = _source.Files.ReadText( display.Content ).Replace( "{categoryId}", categoryId.ToString() );
            _db.Execute( @"
INSERT INTO [HtmlContent] ( [BlockId], [EntityValue], [Version], [Content], [IsApproved], [ApprovedDateTime], [Guid], [ForeignKey], [CreatedDateTime], [ModifiedDateTime] )
VALUES ( @BlockId, '', 1, @Content, 1, @Now, @Guid, @ForeignKey, @Now, @Now )",
                P( "@BlockId", blockId ), P( "@Content", lava ), P( "@Now", now ), P( "@Guid", display.ContentGuid ), P( "@ForeignKey", _foreignKey ) );
        }
    }
}
