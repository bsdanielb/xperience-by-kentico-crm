﻿using CMS.OnlineForms;
using Kentico.Xperience.CRM.Common.Constants;
using Kentico.Xperience.CRM.Common.Mapping.Implementations;
using Kentico.Xperience.CRM.Common.Services;
using Kentico.Xperience.CRM.Common.Services.Implementations;
using Kentico.Xperience.CRM.SalesForce.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SalesForce.OpenApi;
using System.Text.Json;

namespace Kentico.Xperience.CRM.SalesForce.Services;

internal class SalesForceLeadsIntegrationService : LeadsIntegrationServiceCommon, ISalesForceLeadsIntegrationService
{
    private readonly SalesForceBizFormsMappingConfiguration bizFormMappingConfig;    
    private readonly ISalesForceApiService apiService;
    private readonly ILogger<SalesForceLeadsIntegrationService> logger;
    private readonly IFailedSyncItemService failedSyncItemService;

    public SalesForceLeadsIntegrationService(
        SalesForceBizFormsMappingConfiguration bizFormMappingConfig,
        ILeadsIntegrationValidationService validationService,
        IOptionsMonitor<SalesForceIntegrationSettings> integrationSettings,
        ISalesForceApiService apiService,
        ILogger<SalesForceLeadsIntegrationService> logger,
        IFailedSyncItemService failedSyncItemService)
        : base(bizFormMappingConfig, validationService, logger)
    {
        this.bizFormMappingConfig = bizFormMappingConfig;        
        this.apiService = apiService;
        this.logger = logger;
        this.failedSyncItemService = failedSyncItemService;
    }

    protected override async Task CreateLeadAsync(BizFormItem bizFormItem,
        IEnumerable<BizFormFieldMapping> fieldMappings)
    {
        try
        {
            var lead = new LeadSObject();
            MapLead(bizFormItem, lead, fieldMappings);

            lead.LeadSource ??= $"Form {bizFormItem.BizFormInfo.FormDisplayName} - ID: {bizFormItem.ItemID}";
            lead.Company ??= "undefined"; //required field - set to 'undefined' to prevent errors
            if (bizFormMappingConfig.ExternalIdFieldName is { Length: > 0 } externalIdFieldName)
            {
                lead.AdditionalProperties[externalIdFieldName] = FormatExternalId(bizFormItem);
            }

            await apiService.CreateLeadAsync(lead);
        }
        catch (ApiException<ICollection<RestApiError>> e)
        {
            logger.LogError(e, "Create lead failed - api error: {ApiResult}", JsonSerializer.Serialize(e.Result));
            failedSyncItemService.LogFailedLeadItem(bizFormItem, CRMType.SalesForce);
        }
        catch (ApiException<ICollection<ErrorInfo>> e)
        {
            logger.LogError(e, "Create lead failed - api error: {ApiResult}", JsonSerializer.Serialize(e.Result));
            failedSyncItemService.LogFailedLeadItem(bizFormItem, CRMType.SalesForce);
        }
        catch (ApiException e)
        {
            logger.LogError(e, "Create lead failed - unexpected api error");
            failedSyncItemService.LogFailedLeadItem(bizFormItem, CRMType.SalesForce);
        }
    }

    protected override async Task UpdateLeadAsync(BizFormItem bizFormItem,
        IEnumerable<BizFormFieldMapping> fieldMappings)
    {
        try
        {
            string? leadId = await apiService.GetLeadIdByExternalId(bizFormMappingConfig.ExternalIdFieldName!,
                FormatExternalId(bizFormItem));

            if (leadId is not null)
            {
                var lead = new LeadSObject();
                MapLead(bizFormItem, lead, fieldMappings);
                await apiService.UpdateLeadAsync(leadId, lead);
            }
            else
            {
                await CreateLeadAsync(bizFormItem, fieldMappings);
            }
            
            failedSyncItemService.DeleteFailedSyncItem(CRMType.SalesForce, bizFormItem.BizFormClassName, bizFormItem.ItemID);
        }
        catch (ApiException<ICollection<RestApiError>> e)
        {
            logger.LogError(e, "Update lead failed - api error: {ApiResult}", JsonSerializer.Serialize(e.Result));
            failedSyncItemService.LogFailedLeadItem(bizFormItem, CRMType.SalesForce);
        }
        catch (ApiException<ICollection<ErrorInfo>> e)
        {
            logger.LogError(e, "Update lead failed - api error: {ApiResult}", JsonSerializer.Serialize(e.Result));
            failedSyncItemService.LogFailedLeadItem(bizFormItem, CRMType.SalesForce);
        }
        catch (ApiException e)
        {
            logger.LogError(e, "Update lead failed - unexpected api error");
            failedSyncItemService.LogFailedLeadItem(bizFormItem, CRMType.SalesForce);
        }
    }

    protected virtual void MapLead(BizFormItem bizFormItem, LeadSObject lead,
        IEnumerable<BizFormFieldMapping> fieldMappings)
    {
        foreach (var fieldMapping in fieldMappings)
        {
            var formFieldValue = fieldMapping.FormFieldMapping.MapFormField(bizFormItem);
            _ = fieldMapping.CrmFieldMapping switch
            {
                CrmFieldNameMapping m => lead.AdditionalProperties[m.CrmFieldName] = formFieldValue,
                CrmFieldMappingFunction<LeadSObject> m => m.MapCrmField(lead, formFieldValue),
                _ => throw new ArgumentOutOfRangeException(nameof(fieldMapping.CrmFieldMapping), fieldMapping.CrmFieldMapping.GetType(), "Unsupported mapping")
            };
        }
    }
}