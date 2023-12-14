﻿using Kentico.Xperience.CRM.SalesForce.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesForce.OpenApi;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mime;
using SalesForceApiClient = SalesForce.OpenApi.SalesForceApiClient;

namespace Kentico.Xperience.CRM.SalesForce.Services;

internal class SalesForceApiService : ISalesForceApiService
{
    private readonly HttpClient httpClient;
    private readonly ILogger<SalesForceApiService> logger;
    private readonly SalesForceIntegrationSettings integrationSettings;
    private readonly SalesForceApiClient apiClient;

    public SalesForceApiService(
        HttpClient httpClient,
        ILogger<SalesForceApiService> logger,
        IOptionsMonitor<SalesForceIntegrationSettings> integrationSettings
        )
    {
        this.httpClient = httpClient;
        this.logger = logger;
        this.integrationSettings = integrationSettings.CurrentValue;

        apiClient = new SalesForceApiClient(httpClient);
    }

    public async Task<SaveResult> CreateLeadAsync(LeadSObject lead)
    {
        return await apiClient.LeadPOSTAsync(MediaTypeNames.Application.Json, lead);
    }

    public async Task UpdateLeadAsync(string id, LeadSObject leadSObject)
    {
        await apiClient.LeadPATCHAsync(id, MediaTypeNames.Application.Json, leadSObject);
    }

    /// <summary>
    /// Method for get entity by external ID is not generated by BETA OpenApi 3 definition
    /// Could be better solution when this endpoint will be generated in <see cref="SalesForceApiClient"/>
    /// </summary>
    /// <param name="fieldName"></param>
    /// <param name="externalId"></param>
    /// <returns></returns>
    public async Task<string?> GetLeadIdByExternalId(string fieldName, string externalId)
    {
        decimal apiVersion = integrationSettings?.ApiConfig?.ApiVersion ?? 59;
        using var request =
            new HttpRequestMessage(HttpMethod.Get, $"/services/data/v{apiVersion:F1}/sobjects/lead/{fieldName}/{externalId}?fields=Id");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(MediaTypeNames.Application.Json));
        var response = await httpClient.SendAsync(request);

        if (response.IsSuccessStatusCode)
        {
            var lead = await response.Content.ReadFromJsonAsync<LeadSObject>();
            return lead?.Id;
        }
        else if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("Lead not found for external field name: '{ExternalFieldName}' and value: '{ExternalId}'",
                fieldName, externalId);
            return null;
        }
        else
        {
            string responseMessage = await response.Content.ReadAsStringAsync();
            throw new ApiException("Unexpected response", (int)response.StatusCode, responseMessage, null!, null);
        }
    }
}