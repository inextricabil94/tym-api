import { pathToFileURL } from 'node:url';

const arm = 'https://management.azure.com';
const subscription = "concat('/subscriptions/', parameters('subscriptionId'))";
const after = name => ({ [name]: ['Succeeded'] });

function http(method, uri, body, audience = `${arm}/`) {
  const inputs = {
    method, uri,
    authentication: { type: 'ManagedServiceIdentity', audience },
    retryPolicy: { type: 'exponential', count: 4, interval: 'PT10S' }
  };
  if (body !== undefined) {
    inputs.body = body;
    inputs.headers = { 'Content-Type': 'application/json' };
  }
  return { type: 'Http', inputs, runAfter: {} };
}

function list(uri) {
  const action = http('GET', uri);
  action.runtimeConfiguration = {
    paginationPolicy: { minimumItemCount: 100000 },
    secureData: { properties: ['outputs'] }
  };
  return action;
}

function stopApps(name, type, apiVersion, statePath) {
  const listName = `List_${name}`;
  const loopName = `For_each_${name}`;
  const stop = http('POST', `@concat('${arm}', items('${loopName}')?['id'], '/stop?api-version=${apiVersion}')`, {});
  stop.runtimeConfiguration = { secureData: { properties: ['outputs'] } };
  return {
    [listName]: list(`@concat('${arm}', ${subscription}, '/providers/${type}?api-version=${apiVersion}')`),
    [loopName]: {
      type: 'Foreach', foreach: `@body('${listName}')?['value']`,
      runtimeConfiguration: { concurrency: { repetitions: 1 } },
      runAfter: after(listName),
      actions: {
        [`Stop_${name}_if_running`]: {
          type: 'If', expression: `@not(equals(toLower(string(items('${loopName}')?['properties']?['${statePath}'])), 'stopped'))`,
          actions: { [`Stop_${name}`]: stop }, else: { actions: {} }, runAfter: {}
        }
      }
    }
  };
}

export const permissions = [
  'Microsoft.CostManagement/query/read',
  'Microsoft.App/containerApps/read',
  'Microsoft.App/containerApps/stop/action',
  'Microsoft.Web/sites/read',
  'Microsoft.Web/sites/stop/action',
  'Microsoft.Storage/storageAccounts/read',
  'Microsoft.Storage/storageAccounts/blobServices/read',
  'Microsoft.Storage/storageAccounts/blobServices/write'
];

export function createDefinition() {
  const storageUri = "@concat(items('For_each_storage_account')?['properties']?['primaryEndpoints']?['blob'], '?restype=service&comp=properties')";
  const getProperties = http('GET', storageUri, undefined, 'https://storage.azure.com/');
  getProperties.inputs.headers = { 'x-ms-version': '2023-11-03', 'x-ms-date': "@formatDateTime(utcNow(), 'R')" };
  const disable = http('PUT', storageUri,
    '<StorageServiceProperties><StaticWebsite><Enabled>false</Enabled></StaticWebsite></StorageServiceProperties>',
    'https://storage.azure.com/');
  disable.inputs.headers = { ...getProperties.inputs.headers, 'Content-Type': 'application/xml' };

  const query = http('POST', `@concat('${arm}', ${subscription}, '/providers/Microsoft.CostManagement/query?api-version=2025-03-01')`, {
    type: 'ActualCost', timeframe: 'MonthToDate',
    dataset: { granularity: 'None', aggregation: { totalCost: { name: 'PreTaxCost', function: 'Sum' } } }
  });
  const validAlert = "@and(equals(triggerBody()?['schemaId'], 'azureMonitorCommonAlertSchema'), equals(toLower(string(triggerBody()?['data']?['alertContext']?['AlertData']?['BudgetId'])), toLower(concat('/subscriptions/', parameters('subscriptionId'), '/providers/Microsoft.Consumption/budgets/', parameters('budgetName')))), equals(triggerBody()?['data']?['alertContext']?['AlertData']?['ThresholdType'], 'Actual'), equals(triggerBody()?['data']?['alertContext']?['AlertData']?['Unit'], parameters('currency')))";
  const validCost = "@and(empty(body('Query_current_month')?['properties']?['nextLink']), equals(length(body('Query_current_month')?['properties']?['rows']), 1), equals(body('Query_current_month')?['properties']?['columns']?[0]?['name'], 'PreTaxCost'), equals(body('Query_current_month')?['properties']?['columns']?[1]?['name'], 'Currency'), equals(body('Query_current_month')?['properties']?['rows']?[0]?[1], parameters('currency')))";

  return {
    $schema: 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#',
    contentVersion: '1.0.0.0',
    parameters: {
      subscriptionId: { type: 'String' }, budgetName: { type: 'String' },
      currency: { type: 'String', defaultValue: 'EUR' }, monthlyLimit: { type: 'Int', defaultValue: 20 }
    },
    triggers: {
      manual: {
        type: 'Request', kind: 'Http', inputs: { method: 'POST', schema: { type: 'object' } },
        runtimeConfiguration: { concurrency: { runs: 1 }, secureData: { properties: ['inputs', 'outputs'] } }
      }
    },
    actions: {
      Check_budget_identity: {
        type: 'If', expression: validAlert, runAfter: {},
        else: { actions: { Ignore_unrelated_alert: { type: 'Compose', inputs: 'Ignored: not this actual-cost EUR budget.', runAfter: {} } } },
        actions: {
          Query_current_month: query,
          Validate_cost_response: {
            type: 'If', expression: validCost, runAfter: after('Query_current_month'),
            else: { actions: { Invalid_cost_response: { type: 'Terminate', inputs: { runStatus: 'Failed', runError: { code: 'InvalidCostResponse', message: 'Expected one current-month EUR cost row; no shutdown performed.' } }, runAfter: {} } } },
            actions: {
              Check_actual_threshold: {
                type: 'If', expression: "@greaterOrEquals(float(body('Query_current_month')?['properties']?['rows']?[0]?[0]), parameters('monthlyLimit'))", runAfter: {},
                else: { actions: { Below_limit: { type: 'Compose', inputs: 'Current month is below the limit; no changes.', runAfter: {} } } },
                actions: {
                  ...stopApps('container_apps', 'Microsoft.App/containerApps', '2025-07-01', 'runningStatus'),
                  ...stopApps('web_apps', 'Microsoft.Web/sites', '2024-11-01', 'state'),
                  List_storage_accounts: list(`@concat('${arm}', ${subscription}, '/providers/Microsoft.Storage/storageAccounts?api-version=2025-06-01')`),
                  For_each_storage_account: {
                    type: 'Foreach', foreach: "@body('List_storage_accounts')?['value']", runAfter: after('List_storage_accounts'),
                    runtimeConfiguration: { concurrency: { repetitions: 1 } },
                    actions: {
                      Get_blob_properties: getProperties,
                      Disable_if_enabled: {
                        type: 'If', expression: "@equals(xpath(xml(body('Get_blob_properties')), 'string(/StorageServiceProperties/StaticWebsite/Enabled)'), 'true')",
                        runAfter: after('Get_blob_properties'), actions: { Disable_static_website: disable }, else: { actions: {} }
                      }
                    }
                  }
                }
              }
            }
          }
        }
      }
    },
    outputs: {}
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.stdout.write(JSON.stringify({ definition: createDefinition(), permissions }, null, 2));
}
