﻿using CMS;
using CMS.Base;
using CMS.ContactManagement;
using CMS.Core;
using CMS.DataEngine;
using CMS.Helpers;
using CMS.OnlineForms;

using Kentico.Xperience.CRM.Common.Admin;
using Kentico.Xperience.CRM.Common.Constants;
using Kentico.Xperience.CRM.Dynamics;
using Kentico.Xperience.CRM.Dynamics.Synchronization;

using Microsoft.Extensions.DependencyInjection;

[assembly: RegisterModule(typeof(DynamicsIntegrationGlobalEvents))]

namespace Kentico.Xperience.CRM.Dynamics;

/// <summary>
/// Module with BizFormItem and ContactInfo event handlers for Dynamics integration
/// </summary>
internal class DynamicsIntegrationGlobalEvents : Module
{
    public DynamicsIntegrationGlobalEvents() : base(nameof(DynamicsIntegrationGlobalEvents))
    {
    }

    private ICRMModuleInstaller? installer;

    protected override void OnInit(ModuleInitParameters parameters)
    {
        base.OnInit(parameters);

        var services = parameters.Services;

        installer = services.GetRequiredService<ICRMModuleInstaller>();

        ApplicationEvents.Initialized.Execute += InitializeModule;

        BizFormItemEvents.Insert.After += SynchronizeBizFormLead;
        BizFormItemEvents.Update.After += SynchronizeBizFormLead;

        ContactInfo.TYPEINFO.Events.Insert.After += ContactSync;
        ContactInfo.TYPEINFO.Events.Update.After += ContactSync;

        RequestEvents.RunEndRequestTasks.Execute += (_, _) =>
        {
            DynamicsSyncQueueWorker.Current.EnsureRunningThread();
            ContactsSyncFromCRMWorker.Current.EnsureRunningThread();
            FailedItemsWorker.Current.EnsureRunningThread();
        };
    }

    private void InitializeModule(object? sender, EventArgs e)
    {
        installer?.Install(CRMType.Dynamics);
    }

    private static void SynchronizeBizFormLead(object? sender, BizFormItemEventArgs e)
    {
        DynamicsSyncQueueWorker.Current.Enqueue(e.Item);
    }

    private static void ContactSync(object? sender, ObjectEventArgs args)
    {
        if (args.Object is not ContactInfo contactInfo ||
            ValidationHelper.GetBoolean(RequestStockHelper.GetItem("SuppressEvents"), false) ||
            !ValidationHelper.IsEmail(contactInfo.ContactEmail))
            return;

        DynamicsSyncQueueWorker.Current.Enqueue(contactInfo);
    }
}