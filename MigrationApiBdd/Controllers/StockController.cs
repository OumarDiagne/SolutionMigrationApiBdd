using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Services.Interfaces;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MigrationApiBdd.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(Roles = "Admin")]
    public class StockController : ControllerBase
    {
        private readonly IStockService _stockService;

        public StockController(IStockService stockService)
        {
            _stockService = stockService;
        }



        /// <summary>
        /// Reapprovisionne le stock d'un produit            
        /// </summary>
        /// <param name="reapprovisionnerStockDto"></param>
        /// <returns>produit mis à jour</returns>
        // POST api/<StockController>
        [HttpPost("reapprovisionner/{id}")]
        public async Task<ActionResult<ProduitDto>> Reapprovisionner(int id, [FromBody] ReapprovisionnerStockDto reapprovisionnerStockDto, CancellationToken cancellationToken)
        {
            var produitReapprovisionne = await _stockService.ReapprovisionnerStockAsync(id, reapprovisionnerStockDto, cancellationToken);
            return Ok(produitReapprovisionne);
        }

      
    }
}
