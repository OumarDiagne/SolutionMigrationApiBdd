using Microsoft.AspNetCore.Mvc;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Helpers;
using MigrationApiBdd.Models;
using MigrationApiBdd.Services.Classes;
using MigrationApiBdd.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MigrationApiBdd.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class CommandeController : ControllerBase
    {

        private readonly ICommandeService _CommandeService;
        private readonly IClientService _ClientService;     
        private readonly IGestionService _gestionService;

        public CommandeController(ICommandeService commandeService, IClientService clientService, IGestionService gestionService)
        {

 
            _CommandeService = commandeService;
            _ClientService = clientService;
            _gestionService = gestionService;
        }

        /// <summary>
        /// Get all commandes
        /// </summary>
        /// <returns>list of commandes</returns>
        // GET: api/<CommandeController>
        [HttpGet]

        public async Task<ActionResult<IEnumerable<CommandeDto>>> GetCommandesAsync(CancellationToken cancellationToken)
        {
                var commandes = await _CommandeService.GetAllCommandesAsync(cancellationToken);
                return Ok(commandes);
        }

        /// <summary>
        /// Get a specific commande by ID
        /// </summary>
        /// <param name="id"></param>
        /// <returns> commande with the specific ID</returns>
        // GET api/<CommandeController>/5
        [HttpGet("{id}")]
        [ActionName(name: "GetCommandeByIdAsync")]
        public async Task<ActionResult<CommandeDto>> GetCommandeByIdAsync(int id, CancellationToken cancellationToken)
        {

            CommandeDto? commandeDto = await _CommandeService.GetCommandeByIdAsync(id, cancellationToken);
            if (commandeDto == null)
                return NotFound();
            return Ok(commandeDto);
        }

        /// <summary>
        /// Creates a new commande            
        /// </summary>
        /// <param name="commande"></param>
        /// <returns>commande created</returns>
        // POST api/<CommandeController>
        [HttpPost]
        public async Task<ActionResult<CommandeDto>> CreateCommandeAsync([FromBody] CreateCommandeDto createCommandeDto, 
            [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            if(!ModelState.IsValid)
                return BadRequest(ModelState);
            var createdCommande = await _CommandeService.CreateCommandeAsync(createCommandeDto,idempotencyKey, cancellationToken);
            return CreatedAtAction(nameof(GetCommandeByIdAsync), new { id = createdCommande.CommandeId }, createdCommande);
        }

        /// <summary>
        /// update a specific commande by ID
        /// </summary>
        /// <param name="commande"></param>
        /// <param name="id"></param>
        /// <returns>updated commande with the specific ID</returns>
        // PUT api/<CommandeController>/5
        [HttpPut("{id}")]
        public async Task<ActionResult<CommandeDto>> UpdateCommandeAsync(int id,[FromBody] UpdateCommandeDto updateCommandeDto,CancellationToken cancellationToken)
        {
            var updatedCommande = await _CommandeService.UpdateCommandeAsync(id,updateCommandeDto, cancellationToken);

            if (updatedCommande is null)
                return NotFound();

            return Ok(updatedCommande);
        }

        /// <summary>
        /// Deletes a specific commande by ID
        /// </summary>
        /// <param name="id"></param>
        // DELETE api/<CommandeController>/5
        [HttpDelete("delete/{id}")]
        public async Task<ActionResult<int>> ArchiveCommandeAsync(int id, [FromHeader(Name = "If-Match")] string? rowVersion, CancellationToken cancellationToken)
        {
    

            // Call the service to delete the commande
            await _CommandeService.ArchiveCommandeByIdAsync(id, rowVersion, cancellationToken);
            return NoContent();
        }

        [HttpGet("admin-test")]
        [Authorize(Roles = "Admin")]
        public IActionResult AdminTest()
        {
            return Ok(new
            {
                Message = "Accès administrateur accordé."
            });
        }
    }
}
