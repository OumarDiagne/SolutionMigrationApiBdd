using Mapster;
using MigrationApiBdd.Dtos;
using MigrationApiBdd.Models;

public static class MapsterConfig
{
    public static void Register()
    {
        TypeAdapterConfig<byte[], string>.NewConfig()
        .MapWith(source => Convert.ToBase64String(source));

        TypeAdapterConfig<string, byte[]>.NewConfig()
             .MapWith(source => Convert.FromBase64String(source));
        TypeAdapterConfig<ClientDto, Clients>
            .NewConfig()
            .Ignore(dest => dest.ClientId)
            .Ignore(dest => dest.RowVersion);

        TypeAdapterConfig<Clients, ClientDto>
            .NewConfig();

        TypeAdapterConfig<CommandeDto, Commandes>
           .NewConfig()
           .Ignore(dest => dest.CommandeId);

        TypeAdapterConfig<Commandes, CommandeDto>
           .NewConfig();
    }
}   