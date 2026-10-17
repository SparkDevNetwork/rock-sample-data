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

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace RockSampleData.Builder
{
    /// <summary>
    /// The --deliver-photos mode: turns images from the image model into delivered photos. For each
    /// item in a batch file it keeps the source image untouched in photos/originals/, saves a
    /// 400-pixel square JPEG in photos/cast/ or photos/pool/, and adds the item's entry to the end of
    /// photos/manifest.json. See photos/brief.md for the batch format.
    /// </summary>
    internal static class PhotoDelivery
    {
        private const int DeliveredSize = 400;

        private static readonly string[] Folders = { "cast", "pool" };

        private static readonly JpegEncoder Encoder = new JpegEncoder { Quality = 82, ColorType = JpegColorType.YCbCrRatio444 };

        private static readonly JsonSerializerOptions ManifestJson = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            NewLine = "\n"
        };

        /// <summary>
        /// Delivers every photo in the batch file. Returns the number delivered. Throws before
        /// writing anything if any item is invalid, so a bad batch never leaves half a delivery.
        /// </summary>
        public static int Run( string root, string batchPath )
        {
            var photos = Path.Combine( root, "photos" );
            var manifestPath = Path.Combine( photos, "manifest.json" );
            var manifest = JsonNode.Parse( File.ReadAllText( manifestPath ) );
            var images = manifest["images"].AsArray();
            var listed = images.Select( i => i["file"].GetValue<string>() ).ToHashSet( StringComparer.Ordinal );

            var batchFolder = Path.GetDirectoryName( Path.GetFullPath( batchPath ) );
            var items = JsonNode.Parse( File.ReadAllText( batchPath ) ).AsArray()
                .Select( item => new BatchItem( Path.GetFullPath( item["source"].GetValue<string>(), batchFolder ), item["entry"].AsObject() ) )
                .ToList();

            foreach ( var item in items )
            {
                CheckItem( item, photos, listed );
                listed.Add( item.File );
            }

            foreach ( var item in items )
            {
                var original = Path.Combine( photos, "originals", Path.ChangeExtension( item.File, Path.GetExtension( item.Source ).ToLowerInvariant() ) );
                Directory.CreateDirectory( Path.GetDirectoryName( original ) );
                File.Copy( item.Source, original );

                using ( var image = Image.Load( item.Source ) )
                {
                    var sourceSize = $"{image.Width}x{image.Height}";
                    image.Mutate( x => x
                        .AutoOrient()
                        .Resize( new ResizeOptions
                        {
                            Size = new Size( DeliveredSize, DeliveredSize ),
                            Mode = ResizeMode.Crop,
                            Position = AnchorPositionMode.Center,
                            Sampler = KnownResamplers.Lanczos3
                        } ) );

                    // Generated images can carry tool metadata; delivered photos carry none.
                    image.Metadata.ExifProfile = null;
                    image.Metadata.IccProfile = null;
                    image.Metadata.IptcProfile = null;
                    image.Metadata.XmpProfile = null;
                    image.SaveAsJpeg( Path.Combine( photos, item.File ), Encoder );
                    Console.WriteLine( $"  {item.File}: {sourceSize} -> {DeliveredSize} square" );
                }

                images.Add( item.Entry.DeepClone() );
            }

            File.WriteAllText( manifestPath, manifest.ToJsonString( ManifestJson ) + "\n" );
            return items.Count;
        }

        /// <summary>
        /// Throws when an item cannot be delivered: a missing source, a file outside the cast and
        /// pool folders, or a photo that is already listed or already on disk.
        /// </summary>
        private static void CheckItem( BatchItem item, string photos, HashSet<string> listed )
        {
            var folder = item.File.Split( '/' )[0];
            if ( !Folders.Contains( folder ) || !item.File.EndsWith( ".jpg", StringComparison.Ordinal ) || item.File.Count( c => c == '/' ) != 1 )
            {
                throw new InvalidOperationException( $"{item.File}: the file must be cast/<name>.jpg or pool/<name>.jpg." );
            }

            if ( listed.Contains( item.File ) || File.Exists( Path.Combine( photos, item.File ) ) )
            {
                throw new InvalidOperationException( $"{item.File}: photos/manifest.json or the photos folder already has this photo." );
            }

            if ( !File.Exists( item.Source ) )
            {
                throw new FileNotFoundException( $"{item.File}: the source image is missing.", item.Source );
            }
        }

        /// <summary>
        /// One batch item: the image model's file and the manifest entry to record for it.
        /// </summary>
        private sealed record BatchItem( string Source, JsonObject Entry )
        {
            public string File => Entry["file"]?.GetValue<string>() ?? string.Empty;
        }
    }
}
