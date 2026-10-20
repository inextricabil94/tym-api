import test from 'node:test';
import assert from 'node:assert/strict';
import { createDefinition, permissions } from '../../deploy/azure-cost-guard.mjs';

const definition = createDefinition();
const identity = definition.actions.Check_budget_identity;
const validation = identity.actions.Validate_cost_response;
const threshold = validation.actions.Check_actual_threshold;

test('accepts only this actual-cost EUR budget, not forecast or arbitrary requests', () => {
  for (const guard of ['azureMonitorCommonAlertSchema', 'BudgetId', "parameters('subscriptionId')", "parameters('budgetName')", 'ThresholdType', "'Actual'", 'Unit', "parameters('currency')"]) {
    assert.ok(identity.expression.includes(guard), guard);
  }
  assert.equal(Object.keys(definition.triggers).length, 1);
  assert.equal(definition.triggers.manual.inputs.method, 'POST');
  assert.equal(definition.triggers.manual.runtimeConfiguration.concurrency.runs, 1);
});

test('queries real current-month costs and verifies column order and currency before comparing', () => {
  const query = identity.actions.Query_current_month;
  assert.equal(query.inputs.body.timeframe, 'MonthToDate');
  assert.equal(query.inputs.body.type, 'ActualCost');
  assert.equal(query.inputs.body.dataset.aggregation.totalCost.name, 'PreTaxCost');
  for (const guard of ['nextLink', 'rows', 'PreTaxCost', 'Currency', "parameters('currency')"]) assert.ok(validation.expression.includes(guard));
  assert.ok(threshold.expression.includes('greaterOrEquals'));
  assert.ok(threshold.expression.includes("parameters('monthlyLimit')"));
  assert.ok(!threshold.expression.includes('triggerBody'));
  assert.equal(definition.parameters.monthlyLimit.defaultValue, 20);
  assert.equal(definition.parameters.monthlyLimit.type, 'Int');
  assert.equal(definition.parameters.currency.defaultValue, 'EUR');
});

test('discovers both app platforms with pagination and only stops running apps', () => {
  for (const [name, state] of [['container_apps', 'runningStatus'], ['web_apps', 'state']]) {
    const list = threshold.actions[`List_${name}`];
    const loop = threshold.actions[`For_each_${name}`];
    assert.equal(list.runtimeConfiguration.paginationPolicy.minimumItemCount, 100000);
    assert.equal(loop.runtimeConfiguration.concurrency.repetitions, 1);
    assert.ok(loop.actions[`Stop_${name}_if_running`].expression.includes(state));
    const stop = loop.actions[`Stop_${name}_if_running`].actions[`Stop_${name}`];
    assert.equal(stop.inputs.method, 'POST');
    assert.ok(stop.inputs.uri.includes('/stop?api-version='));
    assert.deepEqual(stop.inputs.body, {});
    assert.equal(stop.inputs.authentication.type, 'ManagedServiceIdentity');
  }
});

test('static shutdown uses managed identity and a narrow service-property update, not deletion', () => {
  const loop = threshold.actions.For_each_storage_account;
  const get = loop.actions.Get_blob_properties;
  const condition = loop.actions.Disable_if_enabled;
  const put = condition.actions.Disable_static_website;
  assert.equal(get.inputs.authentication.audience, 'https://storage.azure.com/');
  assert.ok(condition.expression.includes('/StaticWebsite/Enabled'));
  assert.equal(put.inputs.method, 'PUT');
  assert.equal(put.inputs.headers['Content-Type'], 'application/xml');
  assert.equal(put.inputs.body, '<StorageServiceProperties><StaticWebsite><Enabled>false</Enabled></StaticWebsite></StorageServiceProperties>');
  assert.ok(!put.inputs.uri.includes('$web'));
});

test('no startup, deletion, broad contributor role, secrets or data-plane grants', () => {
  assert.equal(permissions.length, 8);
  for (const permission of permissions) assert.doesNotMatch(permission, /\*|delete|listkeys|start\/|roleassignments|blobs\/|containers\//i);
  const json = JSON.stringify(definition);
  assert.doesNotMatch(json, /clientSecret|password|accountKey|\/start\?/i);
  assert.ok(!json.includes('https://prod-'));
  assert.ok(!json.includes('sig='));
});

test('nested action names are globally unique, as required by Azure', () => {
  const names = new Set();
  function visit(actions) {
    for (const [name, action] of Object.entries(actions)) {
      assert.ok(!names.has(name), name);
      names.add(name);
      if (action.actions) visit(action.actions);
      if (action.else?.actions) visit(action.else.actions);
    }
  }
  visit(definition.actions);
});
