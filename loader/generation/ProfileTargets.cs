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

using System.Text.Json;
using System.Text.Json.Nodes;

using RockSampleData.Loader.Packages;

namespace RockSampleData.Loader.Generation
{
    /*
        10/15/26 - CLAUDE

        This is the C# twin of calibration/resolve-profile.js: blend sources by weight,
        remove excluded paths (renormalizing percentage mixes), then apply overrides.
        The builder must not need Node at run time, and both must give the same answer.

        Reason: One profile, resolved the same way by the tools and the builder.
    */

    /// <summary>
    /// The effective calibration targets the generators aim for.
    /// </summary>
    internal sealed class ProfileTargets
    {
        private static readonly HashSet<string> SkippedKeys = new HashSet<string> { "source", "description", "collected", "period", "notes" };

        private readonly JsonObject _values;

        private ProfileTargets( JsonObject values )
        {
            _values = values;
        }

        /// <summary>
        /// Reads targets that were already resolved, as a package's profile.json holds them.
        /// </summary>
        public static ProfileTargets FromJson( string json )
        {
            return new ProfileTargets( JsonNode.Parse( json ).AsObject() );
        }

        /// <summary>
        /// The resolved targets as JSON, for a package's profile.json. Numbers are written in
        /// their shortest exact form, so a load from the package matches a load from the repository.
        /// </summary>
        public string ToJson()
        {
            return _values.ToJsonString( new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" } ) + "\n";
        }

        /// <summary>
        /// Resolves a profile (for example "sample-church") from calibration/profiles in the repository.
        /// </summary>
        public static ProfileTargets Resolve( IPackageFiles repository, string profileName )
        {
            var profile = JsonNode.Parse( repository.ReadText( $"calibration/profiles/{profileName}.json" ) ).AsObject();

            var blend = ( profile["blend"] as JsonArray ) ?? new JsonArray();
            var sectionBlend = profile["sectionBlend"] as JsonObject ?? new JsonObject();
            var sourceIds = blend.Select( b => b["source"].GetValue<string>() )
                .Concat( sectionBlend.SelectMany( s => s.Value.AsArray().Select( b => b["source"].GetValue<string>() ) ) )
                .Distinct()
                .ToList();
            var sources = sourceIds.ToDictionary( id => id, id => JsonNode.Parse( repository.ReadText( $"calibration/profiles/sources/{id}.json" ) ).AsObject() );

            var sections = blend.SelectMany( b => sources[b["source"].GetValue<string>()].Select( kv => kv.Key ) )
                .Concat( sectionBlend.Select( s => s.Key ).Where( k => !k.Contains( '.' ) ) )
                .Where( k => !SkippedKeys.Contains( k ) )
                .Distinct()
                .ToList();

            var effective = new JsonObject();
            foreach ( var section in sections )
            {
                var recipe = sectionBlend[section]?.AsArray() ?? blend;
                var values = recipe.Select( b => sources[b["source"].GetValue<string>()][section] ).ToList();
                var weights = recipe.Select( b => b["weight"].GetValue<double>() ).ToList();
                effective[section] = Blend( values, weights );
            }

            // A dotted key (for example "prayer.monthIndex") replaces one value inside a section with a blend of its own sources.
            foreach ( var dotted in sectionBlend.Where( s => s.Key.Contains( '.' ) ) )
            {
                var parts = dotted.Key.Split( '.' );
                var parent = parts.Take( parts.Length - 1 ).Aggregate( ( JsonNode ) effective, ( node, part ) => node?[part] ) as JsonObject;
                if ( parent == null )
                {
                    continue;
                }

                var recipe = dotted.Value.AsArray();
                var values = recipe.Select( b => parts.Aggregate( ( JsonNode ) sources[b["source"].GetValue<string>()], ( node, part ) => node?[part] ) ).ToList();
                var weights = recipe.Select( b => b["weight"].GetValue<double>() ).ToList();
                parent[parts[^1]] = Blend( values, weights );
            }

            foreach ( var excluded in ( profile["excluded"] as JsonArray ) ?? new JsonArray() )
            {
                Exclude( effective, excluded["path"].GetValue<string>() );
            }

            foreach ( var overrideNode in ( profile["overrides"] as JsonArray ) ?? new JsonArray() )
            {
                Set( effective, overrideNode["path"].GetValue<string>(), overrideNode["value"]?.DeepClone() );
            }

            return new ProfileTargets( effective );
        }

        /// <summary>
        /// Returns the node at a dotted path, or null.
        /// </summary>
        public JsonNode Node( string path )
        {
            JsonNode node = _values;
            foreach ( var part in path.Split( '.' ) )
            {
                node = node is JsonObject obj && obj.TryGetPropertyValue( part, out var child ) ? child : null;
                if ( node == null )
                {
                    return null;
                }
            }

            return node;
        }

        /// <summary>
        /// Returns the number at a dotted path, or the fallback.
        /// </summary>
        public double Number( string path, double fallback )
        {
            return Node( path ) is JsonValue value && value.TryGetValue<double>( out var number ) ? number : fallback;
        }

        /// <summary>
        /// Returns an object of numbers at a dotted path (for example a mix of day of week to percent).
        /// </summary>
        public Dictionary<string, double> Map( string path )
        {
            var result = new Dictionary<string, double>();
            if ( Node( path ) is JsonObject obj )
            {
                foreach ( var kv in obj )
                {
                    if ( kv.Value is JsonValue value && value.TryGetValue<double>( out var number ) )
                    {
                        result[kv.Key] = number;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Blends values from several sources: numbers by weighted average, objects key by key,
        /// and anything else from the first source that has it.
        /// </summary>
        private static JsonNode Blend( List<JsonNode> values, List<double> weights )
        {
            var present = values.Select( ( v, i ) => (Value: v, Weight: weights[i]) ).Where( x => x.Value != null ).ToList();
            if ( present.Count == 0 )
            {
                return null;
            }

            if ( present.All( x => x.Value is JsonValue v && v.TryGetValue<double>( out _ ) ) )
            {
                var total = present.Sum( x => x.Weight );
                return JsonValue.Create( Math.Round( present.Sum( x => x.Value.GetValue<double>() * x.Weight ) / total, 2 ) );
            }

            if ( present.All( x => x.Value is JsonObject ) )
            {
                var keys = present.SelectMany( x => x.Value.AsObject().Select( kv => kv.Key ) ).Distinct().ToList();
                var result = new JsonObject();
                foreach ( var key in keys )
                {
                    result[key] = Blend( present.Select( x => x.Value[key] ).ToList(), present.Select( x => x.Weight ).ToList() );
                }

                return result;
            }

            return present[0].Value.DeepClone();
        }

        /// <summary>
        /// Removes a path. A mix of percentage objects that loses a key is renormalized to 100.
        /// </summary>
        private static void Exclude( JsonObject root, string path )
        {
            var parts = path.Split( '.' );
            var parent = parts.Take( parts.Length - 1 ).Aggregate( ( JsonNode ) root, ( node, part ) => node?[part] ) as JsonObject;
            if ( parent == null || !parent.Remove( parts[^1] ) )
            {
                return;
            }

            var children = parent.Select( kv => kv.Value ).OfType<JsonObject>().ToList();
            if ( children.Count == 0 || children.Count != parent.Count )
            {
                return;
            }

            foreach ( var field in children[0].Select( kv => kv.Key ).Where( k => k.StartsWith( "pct" ) ).ToList() )
            {
                var total = children.Sum( c => c[field]?.GetValue<double>() ?? 0 );
                if ( total > 0 )
                {
                    foreach ( var child in children )
                    {
                        child[field] = Math.Round( ( child[field]?.GetValue<double>() ?? 0 ) * 1000 / total ) / 10;
                    }
                }
            }
        }

        /// <summary>
        /// Sets a path, creating missing parents.
        /// </summary>
        private static void Set( JsonObject root, string path, JsonNode value )
        {
            var parts = path.Split( '.' );
            var node = root;
            foreach ( var part in parts.Take( parts.Length - 1 ) )
            {
                if ( node[part] is not JsonObject child )
                {
                    child = new JsonObject();
                    node[part] = child;
                }

                node = child;
            }

            node[parts[^1]] = value;
        }
    }
}
