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

/*
    11/17/26 - CLAUDE

    Rock's newer email editor saves a section as its own component markup: a section component with nested
    wrapper tables, and child components that each carry a version the editor migrates from. Writing that markup
    by hand would drift from Rock's code, so this spec runs Rock's own editor code (Rock.JavaScript.Obsidian,
    under Jest and jsdom, as Rock's own tests do) and builds each section the way Rock seeds its starter
    sections: a section component, the Rock Solid Church section's Guid as data-email-section-guid, and title,
    text, image, and button components set through each component's own adapter. make-sections.js runs it.

    Reason: The sections open in Rock's editor exactly as one staff built would (decision D-snippets-3).
*/

import {
    buttonComponentAdapter,
    getSectionComponentHelper,
    imageComponentAdapter,
    textComponentAdapter,
    titleComponentAdapter
} from "@Obsidian/Controls/Internal/EmailEditor/utils.partial";

// eslint-disable-next-line @typescript-eslint/no-var-requires
const fs = require( "fs" );
// eslint-disable-next-line @typescript-eslint/no-var-requires
const path = require( "path" );

const repository = path.join( __dirname, "..", "..", ".." );
const library = JSON.parse( fs.readFileSync( path.join( __dirname, "..", "email-sections.json" ), "utf8" ) );
const colors = JSON.parse( fs.readFileSync( path.join( repository, "configuration", "communications.json" ), "utf8" ) ).template.lavaFields;
const events = JSON.parse( fs.readFileSync( path.join( repository, "photos", "events", "manifest.json" ), "utf8" ) ).images;

/** The site links point at, as loader/generation/MessageRenderer.cs writes them. */
const site = "https://rocksolidchurchdemo.com";

/** The branded template's type, from content/communications/email-template-wizard.html. */
const fontFamily = "-apple-system, BlinkMacSystemFont, 'Helvetica Neue', Helvetica, Arial, sans-serif";

/** The width of the template's body, inside its padding, where a section is dropped. */
const contentWidth = 504;

/** The loader fills this in with a line for each of the dataset's campuses. */
const campusServiceTimesToken = "{campusServiceTimes}";

type Section = {
    key: string;
    name: string;
    guid: string;
    card?: "light" | "accent";
    eyebrow?: string;
    title: string;
    text: string;
    serviceTimes?: boolean;
    image?: { event: string; guid: string; alt: string };
    button: { text: string; href: string };
};

function shorthand<T>( top: T, right: T, bottom: T, left: T ): { top: T; right: T; bottom: T; left: T } {
    return { top, right, bottom, left };
}

/**
 * A JPEG's width and height, read from its first start-of-frame marker.
 */
function jpegSize( bytes: Uint8Array ): [ number, number ] {
    let offset = 2;
    while ( offset < bytes.length ) {
        const marker = bytes[offset + 1];
        const length = ( bytes[offset + 2] << 8 ) | bytes[offset + 3];
        if ( marker >= 0xC0 && marker <= 0xCF && marker !== 0xC4 && marker !== 0xC8 && marker !== 0xCC ) {
            return [ ( bytes[offset + 7] << 8 ) | bytes[offset + 8], ( bytes[offset + 5] << 8 ) | bytes[offset + 6] ];
        }
        offset += 2 + length;
    }
    throw new Error( "Not a JPEG with a start-of-frame marker." );
}

function escapeHtml( text: string ): string {
    return text.replace( /&/g, "&amp;" ).replace( /</g, "&lt;" ).replace( />/g, "&gt;" );
}

function textComponent( html: string, fontSizePx: number, textColor: string, marginBottomPx: number, extra: Record<string, unknown> = {} ): HTMLElement {
    const component = textComponentAdapter.createComponentElement( document );
    textComponentAdapter.writeLocalProps( component, {
        ...textComponentAdapter.readLocalProps( component ),
        html,
        fontFamily,
        fontSizePx,
        lineHeight: 1.5,
        textColor,
        marginPx: shorthand( 0, 0, marginBottomPx, 0 ),
        ...extra
    } );
    return component;
}

function buildSection( section: Section ): HTMLElement {
    const helper = getSectionComponentHelper();
    const sectionComponent = helper.createComponentElement( "section" );
    const elements = helper.getElements( sectionComponent );
    if ( !elements ) {
        throw new Error( `Rock's editor made no wrappers for ${section.key}.` );
    }

    // The editor tracks a saved section by its Guid on the component (emailSectionModal.partial.obs).
    sectionComponent.setAttribute( "data-email-section-guid", section.guid.toLowerCase() );

    // A card's background, padding, and corners, set where the section property panel sets them.
    const isAccent = section.card === "accent";
    const paddingWrapper = elements.marginWrapper.borderWrapper.paddingWrapper;
    if ( section.card ) {
        const background = isAccent ? colors.accentColor : colors.pageColor;
        paddingWrapper.td.style.backgroundColor = background;
        paddingWrapper.table.setAttribute( "bgcolor", background );
        sectionComponent.setAttribute( "data-component-background-color", "true" );
        paddingWrapper.td.style.padding = "28px 28px 28px 28px";
        elements.marginWrapper.borderWrapper.td.style.borderRadius = "18px";
    }

    const textColor = isAccent ? "#FFFFFF" : colors.textColor;
    const dropzone = elements.rowWrapper?.querySelector( ".dropzone" );
    if ( !dropzone ) {
        throw new Error( `Rock's section for ${section.key} has no column to drop components into.` );
    }

    if ( section.image ) {
        const photo = events.find( ( e: { event: string } ) => e.event === section.image!.event );
        const width = section.card ? contentWidth - 56 : contentWidth;
        const image = imageComponentAdapter.createComponentElement( document );
        const props = {
            ...imageComponentAdapter.readLocalProps( image ),
            imageSource: { type: "file" as const, isHighResolution: true, file: { value: section.image.guid, text: path.basename( photo.file ) } },
            altText: section.image.alt,
            href: null,
            imageSize: { type: "fixed" as const, resizeMode: "crop" as const, fixedWidthPx: width, fixedHeightPx: Math.round( width / 2 ) },
            horizontalAlignment: "left" as const,
            borderRadiusPx: shorthand( 12, 12, 12, 12 ),
            marginPx: shorthand( 0, 0, 20, 0 ),
            border: null
        };
        imageComponentAdapter.writeLocalProps( image, props );

        // In a browser, the editor reads the photo's natural size once it loads, then sizes the image. jsdom loads no
        // images, so the spec records the size the browser would read and writes the same props again, which sizes it.
        const [ naturalWidth, naturalHeight ] = jpegSize( fs.readFileSync( path.join( repository, "photos", photo.file ) ) );
        image.setAttribute( "data-image-original-width", `${naturalWidth}` );
        image.setAttribute( "data-image-original-height", `${naturalHeight}` );
        imageComponentAdapter.writeLocalProps( image, props );
        dropzone.appendChild( image );
    }

    if ( section.eyebrow ) {
        dropzone.appendChild( textComponent( `<p style="margin: 0;">${escapeHtml( section.eyebrow )}</p>`, 13, isAccent ? "#FFFFFF" : colors.accentColor, 6,
            { isBold: true, letterCase: "uppercase" } ) );
    }

    const title = titleComponentAdapter.createComponentElement( document );
    titleComponentAdapter.writeLocalProps( title, {
        ...titleComponentAdapter.readLocalProps( title ),
        headingLevel: "h2",
        text: section.title,
        fontFamily,
        fontSizePx: 26,
        isBold: true,
        lineHeight: 1.2,
        textColor,
        marginPx: shorthand( 0, 0, 10, 0 ),
        paddingPx: null,
        border: null,
        borderRadiusPx: null
    } );
    dropzone.appendChild( title );

    dropzone.appendChild( textComponent( `<p style="margin: 0;">${escapeHtml( section.text )}</p>`, 17, textColor, section.serviceTimes ? 14 : 20 ) );

    if ( section.serviceTimes ) {
        dropzone.appendChild( textComponent( campusServiceTimesToken, 16, textColor, 20 ) );
    }

    const button = buttonComponentAdapter.createComponentElement( document );
    buttonComponentAdapter.writeLocalProps( button, {
        ...buttonComponentAdapter.readLocalProps( button ),
        text: section.button.text,
        href: `${site}${section.button.href}`,
        fontFamily,
        fontSizePx: 16,
        isBold: true,
        isUnderlined: false,
        textColor: isAccent ? colors.accentColor : "#FFFFFF",
        backgroundColor: isAccent ? "#FFFFFF" : colors.accentColor,
        horizontalAlignment: "left",
        borderRadiusPx: shorthand( 10, 10, 10, 10 ),
        width: { mode: "fitToText", fixedWidthPx: null },
        paddingPx: shorthand( 12, 24, 12, 24 ),
        marginPx: shorthand( 0, 0, 0, 0 ),
        border: null
    } );
    dropzone.appendChild( button );

    return sectionComponent;
}

/**
 * jsdom drops the "important" priority the editor sets on each border wrapper's border-collapse, which a browser keeps,
 * so the markup is written the way a browser saves it.
 */
function asBrowserSavesIt( html: string ): string {
    return html.replace( /(class="border-wrapper[^"]*"[^>]*style="border-collapse: separate)(;")/g, "$1 !important$2" )
        .replace( /(style="border-collapse: separate)(;"[^>]*class="border-wrapper)/g, "$1 !important$2" );
}

test( "builds each email section with Rock's own email editor code", () => {
    for ( const section of library.sections as Section[] ) {
        const html = asBrowserSavesIt( buildSection( section ).outerHTML );
        fs.writeFileSync( path.join( __dirname, `${section.key}.html` ), `${html}\n` );
    }
} );
