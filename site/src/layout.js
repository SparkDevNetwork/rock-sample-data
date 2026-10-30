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
    The shell every page shares. Every link and asset path is relative, so the built site
    works on GitHub Pages and when opened straight from disk (file://) for a local look.
*/

const repoUrl = "https://github.com/SparkDevNetwork/rock-sample-data";

// Link previews in Teams, Slack, and social apps need full URLs, so share tags use the published address.
const siteUrl = "https://sampledata.rockrms.com/";

const nav = [
    { key: "why", label: "Why", href: "why.html" },
    { key: "church", label: "The Church", href: "church.html" },
    { key: "cast", label: "The Cast", href: "cast.html" },
    { key: "features", label: "Features", href: "features.html" },
    { key: "datasets", label: "Datasets", href: "datasets.html" },
    { key: "how", label: "How It's Built", href: "how-its-built.html" },
    { key: "brand", label: "Brand & Downloads", href: "brand.html" },
    { key: "contribute", label: "Contribute", href: "contribute.html" }
];

/**
 * Escapes text for HTML content and attributes.
 */
function esc( value ) {
    return String( value == null ? "" : value )
        .replace( /&/g, "&amp;" )
        .replace( /</g, "&lt;" )
        .replace( />/g, "&gt;" )
        .replace( /"/g, "&quot;" );
}

/**
 * Tagged template that joins arrays, so a page can write ${items.map(...)} without .join("").
 */
function html( strings, ...values ) {
    return strings.reduce( ( out, s, i ) => {
        const v = i < values.length ? values[i] : "";
        return out + s + ( Array.isArray( v ) ? v.join( "" ) : v == null || v === false ? "" : v );
    }, "" );
}

/**
 * Returns a function that turns a site-root path into one relative to a page at this depth.
 */
function relativeTo( depth ) {
    const prefix = "../".repeat( depth );
    return p => prefix + p;
}

function page( { file, title, description, active, depth = 0, body } ) {
    const r = relativeTo( depth );
    const fullTitle = title ? `${title} | Rock Sample Data` : "Rock Sample Data";
    return html`<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${esc( fullTitle )}</title>
<meta name="description" content="${esc( description )}">
<meta property="og:site_name" content="Rock Sample Data">
<meta property="og:title" content="${esc( fullTitle )}">
<meta property="og:description" content="${esc( description )}">
<meta property="og:type" content="website">
<meta property="og:url" content="${esc( siteUrl + ( file && file !== "index.html" ? file : "" ) )}">
<meta property="og:image" content="${siteUrl}assets/share-card.jpg">
<meta property="og:image:width" content="1200">
<meta property="og:image:height" content="630">
<meta property="og:image:alt" content="Rock Solid Church's front entrance with the words: A whole church, ready in seconds.">
<meta name="twitter:card" content="summary_large_image">
<meta name="twitter:image" content="${siteUrl}assets/share-card.jpg">
<meta name="theme-color" content="#FAF7F2" media="(prefers-color-scheme: light)">
<meta name="theme-color" content="#141312" media="(prefers-color-scheme: dark)">
<link rel="icon" href="${r( "assets/favicon.svg" )}" type="image/svg+xml">
<link rel="icon" href="${r( "assets/favicon-32.png" )}" type="image/png" sizes="32x32">
<link rel="apple-touch-icon" href="${r( "assets/apple-touch-icon.png" )}">
<link rel="preconnect" href="https://fonts.googleapis.com">
<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
<link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Figtree:ital,wght@0,400..800;1,400..600&family=Fraunces:ital,opsz,wght@0,9..144,400..600;1,9..144,400..500&family=Montserrat:wght@600&display=swap">
<link rel="stylesheet" href="${r( "assets/styles.css" )}">
<script>document.documentElement.classList.add("js");</script>
<script src="${r( "assets/site.js" )}" defer></script>
</head>
<body>
<a class="skip" href="#main">Skip to content</a>
<header class="site-header">
  <div class="wrap header-row">
    <a class="brand" href="${r( "index.html" )}" aria-label="Rock Sample Data home">
      <img src="${r( "assets/rock/rock-logo-mark.svg" )}" alt="" width="32" height="32">
      <span class="brand-text"><span class="brand-rock">Rock</span> Sample Data</span>
    </a>
    <button class="nav-toggle" type="button" aria-expanded="false" aria-controls="site-nav"><span class="nav-toggle-bars" aria-hidden="true"></span>Menu</button>
    <nav class="site-nav" id="site-nav" aria-label="Main">
      ${nav.map( n => html`<a href="${r( n.href )}"${n.key === active ? ' aria-current="page"' : ""}>${esc( n.label )}</a>` )}
      <a class="nav-github" href="${repoUrl}">GitHub</a>
    </nav>
    <a class="button button-small button-ghost header-cta" href="${repoUrl}">GitHub</a>
  </div>
</header>
<main id="main">
${body}
</main>
<footer class="site-footer">
  <div class="wrap footer-grid">
    <div class="footer-brand">
      <img class="footer-rock" src="${r( "assets/rock/rock-combination-mark-white.svg" )}" alt="Rock RMS" width="150" height="40">
      <p>Rock Sample Data is a Rock RMS project: one realistic church, Rock Solid Church, for testing, teaching, and showing Rock.</p>
      <p class="fine">Every person, family, business, and photo here is fictional. The photos are AI-generated, and no face is meant to look like a real person.</p>
    </div>
    <div>
      <h2 class="footer-heading">Explore</h2>
      <ul class="footer-links">
        ${nav.slice( 0, 4 ).map( n => html`<li><a href="${r( n.href )}">${esc( n.label )}</a></li>` )}
      </ul>
    </div>
    <div>
      <h2 class="footer-heading">Build with it</h2>
      <ul class="footer-links">
        ${nav.slice( 4 ).map( n => html`<li><a href="${r( n.href )}">${esc( n.label )}</a></li>` )}
        <li><a href="${repoUrl}">Source on GitHub</a></li>
      </ul>
    </div>
    <div>
      <h2 class="footer-heading">Rock RMS</h2>
      <ul class="footer-links">
        <li><a href="https://www.rockrms.com">rockrms.com</a></li>
        <li><a href="https://community.rockrms.com">Rock Community</a></li>
        <li><a href="${repoUrl}/blob/main/LICENSE.md">Rock Community License</a></li>
      </ul>
    </div>
  </div>
  <div class="wrap footer-base">
    <span>Copyright Spark Development Network. Licensed under the Rock Community License.</span>
  </div>
</footer>
</body>
</html>
`;
}

module.exports = { page, esc, html, relativeTo, repoUrl };
