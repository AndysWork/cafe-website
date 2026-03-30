using Cafe.Api.Models;

namespace Cafe.Api.Repositories;

public interface IOfferRepository
{
    Task<List<Offer>> GetActiveOffersAsync();
    Task<List<Offer>> GetAllOffersAsync();
    Task<Offer?> GetOfferByIdAsync(string id);
    Task<Offer?> GetOfferByCodeAsync(string code);
    Task<Offer> CreateOfferAsync(Offer offer);
    Task<bool> UpdateOfferAsync(string id, Offer offer);
    Task<bool> DeleteOfferAsync(string id);
    Task<bool> IncrementOfferUsageAsync(string id);
    Task<OfferValidationResponse> ValidateOfferAsync(OfferValidationRequest request);
}
