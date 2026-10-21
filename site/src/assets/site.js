/* Rock Sample Data site. Copyright by the Spark Development Network; Licensed under the Rock Community License. */

( function () {
    "use strict";

    var header = document.querySelector( ".site-header" );
    function onScroll() {
        if ( header ) {
            header.classList.toggle( "is-scrolled", window.scrollY > 8 );
        }
    }
    window.addEventListener( "scroll", onScroll, { passive: true } );
    onScroll();

    // Fade sections in as they scroll into view.
    var reveals = document.querySelectorAll( ".reveal" );
    if ( "IntersectionObserver" in window ) {
        var observer = new IntersectionObserver( function ( entries ) {
            entries.forEach( function ( entry ) {
                if ( entry.isIntersecting ) {
                    entry.target.classList.add( "is-visible" );
                    observer.unobserve( entry.target );
                }
            } );
        }, { rootMargin: "0px 0px -8% 0px", threshold: 0.08 } );
        reveals.forEach( function ( el ) { observer.observe( el ); } );
    }
    else {
        reveals.forEach( function ( el ) { el.classList.add( "is-visible" ); } );
    }

    // Cast gallery: search and life-stage filters.
    var grid = document.querySelector( "[data-cast-grid]" );
    if ( grid ) {
        var cards = Array.prototype.slice.call( grid.querySelectorAll( ".household-card" ) );
        var search = document.querySelector( "[data-cast-search]" );
        var count = document.querySelector( "[data-cast-count]" );
        var empty = document.querySelector( "[data-cast-empty]" );
        var chips = document.querySelectorAll( "[data-filter]" );
        var filter = "all";

        var apply = function () {
            var words = ( search.value || "" ).toLowerCase().trim().split( /\s+/ ).filter( Boolean );
            var shown = 0;
            cards.forEach( function ( card ) {
                var tags = " " + card.getAttribute( "data-filters" ) + " ";
                var text = card.getAttribute( "data-search" );
                var match = ( filter === "all" || tags.indexOf( " " + filter + " " ) >= 0 ) && words.every( function ( w ) { return text.indexOf( w ) >= 0; } );
                card.hidden = !match;
                if ( match ) {
                    shown++;
                    card.classList.add( "is-visible" );
                }
            } );
            count.textContent = shown + ( shown === 1 ? " household" : " households" );
            empty.hidden = shown > 0;
        };

        chips.forEach( function ( chip ) {
            chip.addEventListener( "click", function () {
                filter = chip.getAttribute( "data-filter" );
                chips.forEach( function ( c ) { c.setAttribute( "aria-pressed", String( c === chip ) ); } );
                apply();
            } );
        } );
        search.addEventListener( "input", apply );
    }

    // Photo viewer.
    var viewer = document.querySelector( ".viewer" );
    if ( viewer && typeof viewer.showModal === "function" ) {
        var viewerImage = viewer.querySelector( "img" );
        var viewerCaption = viewer.querySelector( ".viewer-caption" );
        document.addEventListener( "click", function ( e ) {
            var button = e.target.closest( "[data-zoom]" );
            if ( !button ) {
                return;
            }
            viewerImage.src = button.getAttribute( "data-zoom" );
            viewerImage.alt = button.querySelector( "img" ).alt;
            viewerCaption.textContent = button.getAttribute( "data-caption" );
            viewer.showModal();
        } );
        viewer.addEventListener( "click", function ( e ) {
            if ( e.target === viewer ) {
                viewer.close();
            }
        } );
    }
    else {
        // Without a dialog, the button opens the photo itself.
        document.querySelectorAll( "[data-zoom]" ).forEach( function ( b ) {
            b.addEventListener( "click", function () { window.open( b.getAttribute( "data-zoom" ), "_blank" ); } );
        } );
    }

    // Click a color swatch to copy its value.
    document.querySelectorAll( "[data-copy]" ).forEach( function ( swatch ) {
        swatch.addEventListener( "click", function () {
            var value = swatch.getAttribute( "data-copy" );
            var done = function () {
                swatch.classList.add( "is-copied" );
                setTimeout( function () { swatch.classList.remove( "is-copied" ); }, 1600 );
            };
            if ( navigator.clipboard && window.isSecureContext ) {
                navigator.clipboard.writeText( value ).then( done, done );
            }
            else {
                var input = document.createElement( "textarea" );
                input.value = value;
                document.body.appendChild( input );
                input.select();
                try { document.execCommand( "copy" ); } catch ( err ) { /* Copy is a convenience; the value is shown on the swatch. */ }
                input.remove();
                done();
            }
        } );
    } );
}() );
