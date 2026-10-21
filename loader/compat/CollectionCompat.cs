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

#if NETFRAMEWORK

namespace System.Collections.Generic
{
    /*
        10/21/26 - CLAUDE

        .NET Framework lacks a few collection helpers that the loader uses all over. These match the
        .NET 10 methods of the same names, so every call site compiles unchanged on both targets. The
        namespace is System.Collections.Generic, so no file needs a new using.

        Reason: The loader builds for Rock's .NET Framework 4.7.2 too (decision D-rock-loader-1).
    */

    /// <summary>
    /// The .NET 10 dictionary and key-value pair helpers the loader uses, for .NET Framework.
    /// </summary>
    internal static class CollectionCompat
    {
        /// <summary>
        /// The value for a key, or the type's default when the key is missing.
        /// </summary>
        public static TValue GetValueOrDefault<TKey, TValue>( this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key )
        {
            return dictionary.TryGetValue( key, out var value ) ? value : default;
        }

        /// <summary>
        /// The value for a key, or <paramref name="defaultValue"/> when the key is missing.
        /// </summary>
        public static TValue GetValueOrDefault<TKey, TValue>( this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue )
        {
            return dictionary.TryGetValue( key, out var value ) ? value : defaultValue;
        }

        /// <summary>
        /// Lets <c>foreach ( var (key, value) in dictionary )</c> work.
        /// </summary>
        public static void Deconstruct<TKey, TValue>( this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value )
        {
            key = pair.Key;
            value = pair.Value;
        }
    }
}

#endif
