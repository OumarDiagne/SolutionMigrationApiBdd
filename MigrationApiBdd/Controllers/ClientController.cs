using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Models;
using MigrationApiBdd.Services.Classes;
using MigrationApiBdd.Services.Interfaces;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace MigrationApiBdd.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ClientController : ControllerBase
    {

        private readonly IClientService _clientService;

        public ClientController(IClientService clientService)
        {

            _clientService = clientService;
        }

        /// <summary>
        /// Get all clients
        /// </summary>
        /// <returns>list of clients</returns>
        // GET: api/<ClientController>
        [HttpGet]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<IEnumerable<ClientDto>>> GetClientsAsync()
        {
         
                List<ClientDto> clients = await _clientService.GetAllClientsAsync();  
                return Ok(clients);
        }



        /// <summary>
        /// Client métier relié au compte connecté (relation 0..1 : un client peut exister sans compte).
        /// </summary>
        // GET api/<ClientController>/me
        [HttpGet("me")]
        public async Task<ActionResult<ClientDto>> GetMyClient(CancellationToken cancellationToken)
        {
            ClientDto? client = await _clientService.GetMyClientAsync(cancellationToken);
            if (client == null)
                return NotFound();
            return Ok(client);
        }

        /// <summary>
        /// Get a specific client by ID
        /// </summary>
        /// <param name="id">The ID of the client</param>
        /// <returns> client with the specific ID</returns>
        // GET api/<ClientController>/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ClientDto>> GetClientById(int id)
        {

            ClientDto? clientDto = await _clientService.GetClientByIdAsync(id);
            if (clientDto == null)
                return NotFound();
            return Ok(clientDto);

        }
        /// <summary>
        /// Get a list of commandes by client ID
        /// </summary>
        /// <param name="id">The ID of the client</param>
        /// <returns> list of commandes for the specific client</returns>
        // GET api/<ClientController>/id/commande/5
        [HttpGet("{id}/commandes")]
        public async Task<ActionResult<IEnumerable<ClientDto>>> GetCommandesClientByIdAsync(int id, CancellationToken cancellationToken)
        {
            ClientDto? client = await _clientService.GetClientByIdAsync(id);
            if (client == null)
                return NotFound();

            List<ClientDto> commandes = await _clientService.GetCommandesClientByIdAsync(id, cancellationToken);
            return Ok(commandes);

        }

        /// <summary>
        /// Creates a new client            
        /// </summary>
        /// <param name="client">The client data to create</param>
        /// <returns>client created</returns>
        // POST api/<ClientController>
        [HttpPost]
        [Authorize(Roles = "Admin")]
        public async Task<ActionResult<ClientDto>> CreateClientAsync([FromBody] CreateClientDto createClientDto,CancellationToken cancellationToken)
        {
            var createdClient = await _clientService.CreateClientAsync(createClientDto,cancellationToken);
            return CreatedAtAction(nameof(GetClientById), new { id = createdClient.ClientId }, createdClient);
        }

        /// <summary>
        /// update a specific client by ID
        /// </summary>
        /// <param name="client">The updated client data</param>
        /// <param name="id">The ID of the client to update</param>
        /// <returns>updated client with the specific ID</returns>
        // PUT api/<ClientController>/5
        [HttpPut("{id}")]
        public async Task<ActionResult<ClientDto>> UpdateClientByIdAsync([FromBody] UpdateClientDto updateClientDto, int id, CancellationToken cancellationToken)
        {
            var updatedClient = await _clientService.UpdateClientAsync(id, updateClientDto, cancellationToken);
            if(updatedClient == null) return NotFound();
            return Ok(updatedClient);
        }

        /// <summary>
        /// Deletes a specific client by ID
        /// </summary>
        /// <param name="id">The ID of the client to delete</param>
        /// <param name="rowVersion">The row version for concurrency control</param>
        // DELETE api/<ClientController>/5
        [HttpDelete("{id}")]
        public async Task<ActionResult<int>> DeleteClientByIdAsync(int id, [FromHeader(Name = "If-Match")] string? rowVersion, CancellationToken cancellationToken)
        {
            // Call the service to delete the client
            await _clientService.DeleteClientByIdAsync(id,rowVersion, cancellationToken);   
            return NoContent();

        }
    }
}
