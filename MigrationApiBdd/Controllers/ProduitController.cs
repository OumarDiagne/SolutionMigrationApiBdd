using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Services.Interfaces;

namespace MigrationApiBdd.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ProduitController : ControllerBase
    {
        private readonly IProduitService _produitService;

        public ProduitController(IProduitService produitService)
        {
            _produitService = produitService;
        }

        /// <summary>
        /// Liste des produits (filtre optionnel sur le nom). Résultat mis en cache 5 min.
        /// </summary>
        // GET: api/Produit?nomProduit=xxx
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public async Task<ActionResult<ICollection<ProduitDto>>> GetProduitsAsync([FromQuery(Name = "nomProduit")] string? nomProduit, CancellationToken cancellationToken)
        {
            ICollection<ProduitDto> produits = await _produitService.GetAllProduitsAsync(nomProduit, cancellationToken);
            return Ok(produits);
        }

        /// <summary>
        /// Détail d'un produit. La RowVersion renvoyée sert pour le PUT et le header If-Match du DELETE.
        /// </summary>
        // GET api/Produit/5
        [HttpGet("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<ProduitDto>> GetProduit(int id, CancellationToken cancellationToken)
        {
            ProduitDto? produit = await _produitService.GetProduitByIdAsync(id, cancellationToken);
            if (produit is null)
                return NotFound();

            return Ok(produit);
        }

        /// <summary>
        /// Crée un produit (Admin). Header Idempotency-Key obligatoire :
        /// même clé + même contenu = même réponse, sans doublon.
        /// </summary>
        // POST api/Produit
        [HttpPost]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        public async Task<ActionResult<ProduitDto>> Post([FromBody] CreateProduitDto createProduitDto,
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            ProduitDto createdProduitDto = await _produitService.CreateProduitAsync(createProduitDto, idempotencyKey, cancellationToken);
            return CreatedAtAction(nameof(GetProduit), new { id = createdProduitDto.ProduitId }, createdProduitDto);
        }

        /// <summary>
        /// Modifie un produit (Admin). La RowVersion du dernier GET est obligatoire dans le corps.
        /// </summary>
        // PUT api/Produit/5
        [HttpPut("{id}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<ActionResult<ProduitDto>> Put(int id, [FromBody] UpdateProduitDto updateProduitDto, CancellationToken cancellationToken)
        {
            ProduitDto updatedProduit = await _produitService.UpdateProduitAsync(id, updateProduitDto, cancellationToken);
            return Ok(updatedProduit);
        }

        /// <summary>
        /// Archive un produit (Admin) : EstDisponible = false. Header If-Match = RowVersion obligatoire.
        /// </summary>
        // DELETE api/Produit/5
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
        public async Task<IActionResult> Delete(int id, [FromHeader(Name = "If-Match")] string? rowVersion, CancellationToken cancellationToken)
        {
            await _produitService.ArchiveProduitByIdAsync(id, rowVersion, cancellationToken);
            return NoContent();
        }
    }
}
