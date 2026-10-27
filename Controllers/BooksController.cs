using Microsoft.AspNetCore.Mvc;
using WebApiProjectWithMongoDB.Models;
using WebApiProjectWithMongoDB.Services;

namespace WebApiProjectWithMongoDB.Controllers;

/// <summary>
/// Example CRUD endpoints over the books collection.
///
/// Notice how little MongoDB there is in here. All the driver work lives in
/// <see cref="BookService"/>, so the controller only deals in HTTP concerns:
/// status codes, model binding and validation. Keeping that boundary clean means
/// you can change how data is stored without touching your API surface.
/// </summary>
[ApiController]
// Spelled out in lowercase rather than using the [controller] token. The token
// would give you "/Books", because it substitutes the class name verbatim - and
// while routing itself is case-insensitive, that capital B leaks into your
// generated Location headers and your OpenAPI document. Lowercase paths are the
// REST norm, so it's worth the small loss of refactor-safety here.
[Route("books")]
[Produces("application/json")]
// Constructor injection via a primary constructor (C# 12). BookService is registered
// in MongoDbRegistration.cs; controllers themselves are created per request by the
// framework. The parameter is captured and usable throughout the class, which saves
// the field-plus-assignment boilerplate.
public sealed class BooksController(BookService books) : ControllerBase
{

    /// <summary>Lists books, newest first.</summary>
    [HttpGet(Name = "GetBooks")]
    [ProducesResponseType<IEnumerable<BookResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<BookResponse>>> Get(
        CancellationToken cancellationToken,
        [FromQuery] int skip = 0,
        [FromQuery] int limit = 20)
    {
        // Clamp the page size. Never let a caller decide how much of your
        // database to pull into memory in one request.
        limit = Math.Clamp(limit, 1, 100);
        skip = Math.Max(skip, 0);

        var results = await books.GetAsync(skip, limit, cancellationToken);

        return Ok(results.Select(BookResponse.From));
    }

    /// <summary>Fetches a single book by its ObjectId.</summary>
    [HttpGet("{id}", Name = "GetBookById")]
    [ProducesResponseType<BookResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookResponse>> GetById(string id, CancellationToken cancellationToken)
    {
        var book = await books.GetByIdAsync(id, cancellationToken);

        // NotFound() produces a bare 404. If you'd rather send a problem+json body
        // explaining why, use Problem(statusCode: 404, detail: "...") instead.
        return book is null ? NotFound() : Ok(BookResponse.From(book));
    }

    /// <summary>Finds books carrying a given tag. Served by a multikey index.</summary>
    [HttpGet("by-tag/{tag}", Name = "GetBooksByTag")]
    [ProducesResponseType<IEnumerable<BookResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<BookResponse>>> GetByTag(
        string tag,
        CancellationToken cancellationToken,
        [FromQuery] int limit = 20)
    {
        var results = await books.GetByTagAsync(tag, Math.Clamp(limit, 1, 100), cancellationToken);

        return Ok(results.Select(BookResponse.From));
    }

    /// <summary>Projection example: ids and titles only, no full documents.</summary>
    [HttpGet("titles", Name = "GetBookTitles")]
    [ProducesResponseType<IEnumerable<BookTitle>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<BookTitle>>> GetTitles(
        CancellationToken cancellationToken,
        [FromQuery] int limit = 100)
    {
        var results = await books.GetTitlesAsync(Math.Clamp(limit, 1, 500), cancellationToken);

        return Ok(results);
    }

    /// <summary>Aggregation example: book and page counts grouped by author.</summary>
    [HttpGet("stats/by-author", Name = "GetBookStatsByAuthor")]
    [ProducesResponseType<IEnumerable<AuthorSummary>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<AuthorSummary>>> GetStatsByAuthor(
        CancellationToken cancellationToken)
    {
        var results = await books.GetBooksPerAuthorAsync(cancellationToken);

        return Ok(results);
    }

    /// <summary>Creates a book. MongoDB generates the ObjectId.</summary>
    [HttpPost(Name = "CreateBook")]
    [ProducesResponseType<BookResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<BookResponse>> Create(
        BookRequest request,
        CancellationToken cancellationToken)
    {
        // No validation code needed here. [ApiController] runs the DataAnnotations
        // attributes on BookRequest automatically and short-circuits with a 400
        // problem+json response before this method is ever called.
        var created = await books.CreateAsync(request.ToBook(), cancellationToken);

        // 201 with a Location header pointing at the new resource. CreatedAtRoute
        // builds that URL from the route name, so it stays correct even if you
        // later change the route template.
        return CreatedAtRoute(
            "GetBookById",
            new { id = created.Id },
            BookResponse.From(created));
    }

    /// <summary>Replaces a book in full.</summary>
    [HttpPut("{id}", Name = "ReplaceBook")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Replace(
        string id,
        BookRequest request,
        CancellationToken cancellationToken)
    {
        var updated = await books.UpdateAsync(id, request.ToBook(), cancellationToken);

        return updated ? NoContent() : NotFound();
    }

    /// <summary>Partial update example: sets a single field with $set.</summary>
    [HttpPatch("{id}/page-count", Name = "UpdateBookPageCount")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdatePageCount(
        string id,
        [FromQuery] int pageCount,
        CancellationToken cancellationToken)
    {
        var updated = await books.UpdatePageCountAsync(id, pageCount, cancellationToken);

        return updated ? NoContent() : NotFound();
    }

    /// <summary>Deletes a book by id.</summary>
    [HttpDelete("{id}", Name = "DeleteBook")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var deleted = await books.DeleteAsync(id, cancellationToken);

        return deleted ? NoContent() : NotFound();
    }
}
