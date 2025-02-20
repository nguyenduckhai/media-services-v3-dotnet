﻿# Copyright (c) Microsoft Corporation. All rights reserved.
# Licensed under the MIT License.

param(
    $ResourceGroupName = 'hatest',
    $ResourceGroupLocation = 'eastus'
)

Write-Host 'Creating resource group.'
New-AzResourceGroup -Name $ResourceGroupName -Location $ResourceGroupLocation -Force

# Deploy the Azure Media Services instances.
Write-Host 'Running main ARM template deployment...'
Write-Host 'Often this step fails the first time it executes because the managed identity does not provision successfully. If this happens, the script will retry the deployment.'
$ErrorActionPreference = 'Continue' # Due to the issue provisioning new managed identities, we will temporarily allow errors to continue for this section of the script
$numberOfRetries = 3;
while ($numberOfRetries -gt 0)
{
    $mainDeployment = New-AzResourceGroupDeployment -ResourceGroupName $ResourceGroupName -TemplateFile './ARMDeployScripts/main.json' -TemplateParameterFile './ARMDeployScripts/all.parameters.json' -Verbose
    if ($null -ne $mainDeployment.Outputs)
    {
        break
    }
    Write-Host 'Retrying Azure Functions app resources deployment in 30 seconds.'
    Start-Sleep -Seconds 30
    $numberOfRetries--;
}
$ErrorActionPreference = 'Stop'

if ($numberOfRetries -eq 0)
{
    throw "Failed to deploy ARM template"
}

$createdFunctionNames = $mainDeployment.Outputs['functionNames'].Value
Write-Host "Created following azure functions: $createdFunctionNames"

$functionFolders = @{JobScheduling="HighAvailability.JobScheduling"; JobOutputStatus="HighAvailability.JobOutputStatus"; Provisioning="HighAvailability.Provisioning"; JobVerification="HighAvailability.JobVerification"; InstanceHealth="HighAvailability.InstanceHealth";}

. dotnet publish HighAvailability.sln

$storageAccountName = $mainDeployment.Outputs['storageAccountName'].Value
$storageAccount = Get-AzStorageAccount -ResourceGroupName $ResourceGroupName -Name $storageAccountName

$queueNames = "job-requests", "job-verification-requests", "provisioning-requests", "provisioning-completed-events";
$queues = Get-AzStorageQueue -Context $storageAccount.Context

foreach ($queueName in $queueNames)
{
    $queueExists = $false
    foreach ($queue in $queues) 
    { 
        if ($queue.Name -eq $queueName)
        {
            $queueExists = $true
        }
    }

    if ($queueExists -eq $false)
    {
        New-AzStorageQueue -Name $queueName -Context $storageAccount.Context
    }
}


foreach ($functionName in $createdFunctionNames)
{
    Write-Host "function name:" $functionName["fullName"].Value "function local folder:" $functionFolders[$functionName["function"].Value]
    $functionZipFilePath = (New-TemporaryFile).FullName + '.zip'
    $publishPath = $functionFolders[$functionName["function"].Value] + '/bin/Debug/netcoreapp3.1/publish/*'
    
    Write-Host "Creating zip file to publish from:" $publishPath 
    Compress-Archive -Path $publishPath -DestinationPath $functionZipFilePath -Force

    Write-Host "Publishing Azure Function:" $functionName["fullName"].Value
    Publish-AzWebApp -Timeout 300000 -ResourceGroupName $ResourceGroupName -Name $functionName["fullName"].Value -ArchivePath $functionZipFilePath -Force

    Restart-AzWebApp -ResourceGroupName $ResourceGroupName -Name $functionName["fullName"].Value
}

Write-Host 'Running event grid setup ARM template deployment...'
$eventGridDeployment = New-AzResourceGroupDeployment -ResourceGroupName $ResourceGroupName -TemplateFile './ARMDeployScripts/eventgridsetup.json' -TemplateParameterFile './ARMDeployScripts/all.parameters.json' -Verbose 

$keyVaultName = $mainDeployment.Outputs['keyVaultName'].Value
Write-Host "This is keyvault name, use this to update E2ETests.cs file to submit sample requests: $keyVaultName"
