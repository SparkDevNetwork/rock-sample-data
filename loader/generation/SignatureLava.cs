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

namespace RockSampleData.Loader.Generation
{
    /*
        10/29/26 - CLAUDE

        Rock's registration entry renders a release's Lava with a Registration and a Registrant merge object and stores
        the result as the signed text (ElectronicSignatureHelper.GetSignatureDocumentHtml). The loader cannot run Lava,
        so a release may use only the four outputs below, which Lava prints as plain values with the text around them
        untouched. Anything else is an error, never a guess. Registrant.FirstName is the person's first name, not
        their nick name, as Rock's LavaSignatureRegistrant sets it.

        Reason: The signed text, and so the hash, is what Rock would have stored.
    */

    /// <summary>
    /// Renders a release's Lava for one registrant.
    /// </summary>
    internal static class SignatureLava
    {
        public const string FirstName = "{{ Registrant.FirstName }}";
        public const string LastName = "{{ Registrant.LastName }}";
        public const string InstanceName = "{{ Registration.InstanceName }}";
        public const string OrganizationName = "{{ 'Global' | Attribute:'OrganizationName' }}";

        /// <summary>
        /// The release's text for one registrant, as Rock's Lava prints it.
        /// </summary>
        /// <exception cref="InvalidDataException">The template uses Lava the loader does not render.</exception>
        public static string Render( string template, string firstName, string lastName, string instanceName, string organizationName )
        {
            var text = Check( template )
                .Replace( FirstName, firstName )
                .Replace( LastName, lastName )
                .Replace( InstanceName, instanceName )
                .Replace( OrganizationName, organizationName );
            return text;
        }

        /// <summary>
        /// The template, once it is known to use only the outputs <see cref="Render"/> fills.
        /// </summary>
        /// <exception cref="InvalidDataException">The template uses Lava the loader does not render.</exception>
        public static string Check( string template )
        {
            var rest = template.Replace( FirstName, string.Empty ).Replace( LastName, string.Empty ).Replace( InstanceName, string.Empty ).Replace( OrganizationName, string.Empty );
            if ( rest.Contains( "{{" ) || rest.Contains( "{%" ) )
            {
                throw new InvalidDataException( $"configuration/registrations.json: a release's lavaTemplate uses Lava the loader cannot render. It may use only {FirstName}, {LastName}, {InstanceName}, and {OrganizationName}." );
            }

            return template;
        }
    }
}
