import ts from 'typescript';
import {
  FORBIDDEN_IDENTIFIERS,
  FORBIDDEN_JSX_ATTRIBUTES,
  FORBIDDEN_JSX_ELEMENTS,
  FORBIDDEN_MEMBER_NAMES,
  GLOBAL_OBJECTS,
  SANCTIONED_IMPORT_SPECIFIERS,
  type GateViolation,
  type ViolationCode,
} from './contract';

// AST-based containment analysis of a generated TSX skin file. We parse with the TypeScript
// compiler API (the toolchain's own parser — not brittle regex) and walk the tree so a
// violation is judged by syntactic role, not textual coincidence: `fetch` as a call is a
// network escape, but `fetch` as a JSX text node or an object *property* name is not.

const forbiddenIdentifiers = new Set<string>(FORBIDDEN_IDENTIFIERS);
const forbiddenMembers = new Set<string>(FORBIDDEN_MEMBER_NAMES);
const forbiddenElements = new Set<string>(FORBIDDEN_JSX_ELEMENTS);
const forbiddenAttributes = new Set<string>(FORBIDDEN_JSX_ATTRIBUTES);
const sanctionedImports = new Set<string>(SANCTIONED_IMPORT_SPECIFIERS);
const globalObjects = new Set<string>(GLOBAL_OBJECTS);

// `fetch`/`eval` etc. get a dedicated code so the report reads clearly.
const IDENTIFIER_CODES: Record<string, ViolationCode> = {
  fetch: 'network-access',
  XMLHttpRequest: 'network-access',
  WebSocket: 'network-access',
  EventSource: 'network-access',
  importScripts: 'network-access',
  eval: 'eval-usage',
  Function: 'eval-usage',
  require: 'forbidden-import',
};

export function analyzeSkinTsx(fileRel: string, source: string): GateViolation[] {
  const violations: GateViolation[] = [];
  const sourceFile = ts.createSourceFile(fileRel, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);

  const lineOf = (node: ts.Node): number =>
    sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile)).line + 1;
  const add = (code: ViolationCode, node: ts.Node, message: string) =>
    violations.push({ code, file: fileRel, message, line: lineOf(node) });

  const visit = (node: ts.Node): void => {
    checkImport(node, add);
    checkDynamicImportAndRequire(node, add);
    checkIdentifier(node, add);
    checkMemberAccess(node, add);
    checkNewExpression(node, add);
    checkJsx(node, add);
    ts.forEachChild(node, visit);
  };
  visit(sourceFile);

  return violations;
}

type Add = (code: ViolationCode, node: ts.Node, message: string) => void;

function checkImport(node: ts.Node, add: Add): void {
  if (!ts.isImportDeclaration(node)) return;
  if (!ts.isStringLiteral(node.moduleSpecifier)) return;
  const spec = node.moduleSpecifier.text;
  if (sanctionedImports.has(spec)) return;
  if (spec.startsWith('.')) {
    if (spec.includes('..')) {
      add('relative-core-import', node, `Generated code may not import into the core via "${spec}". Import types only from the sanctioned contract module(s): ${[...sanctionedImports].join(', ')}.`);
    } else {
      add('forbidden-import', node, `Import "${spec}" is not a sanctioned contract module. Allowed: ${[...sanctionedImports].join(', ')}.`);
    }
    return;
  }
  add('new-dependency', node, `Generated code may not add the npm dependency "${spec}". Import types only from the sanctioned contract module(s): ${[...sanctionedImports].join(', ')}.`);
}

function checkDynamicImportAndRequire(node: ts.Node, add: Add): void {
  if (!ts.isCallExpression(node)) return;
  if (node.expression.kind === ts.SyntaxKind.ImportKeyword) {
    add('dynamic-import', node, 'Dynamic import() is not allowed in generated code.');
  }
}

function isDeclarationName(node: ts.Identifier): boolean {
  const parent = node.parent;
  return (
    (ts.isPropertyAccessExpression(parent) && parent.name === node) ||
    (ts.isPropertyAssignment(parent) && parent.name === node) ||
    (ts.isBindingElement(parent) && parent.propertyName === node) ||
    ((ts.isImportSpecifier(parent) || ts.isExportSpecifier(parent)) && (parent.name === node || parent.propertyName === node)) ||
    (ts.isParameter(parent) && parent.name === node) ||
    (ts.isVariableDeclaration(parent) && parent.name === node) ||
    (ts.isFunctionDeclaration(parent) && parent.name === node) ||
    ts.isPropertySignature(parent)
  );
}

function checkIdentifier(node: ts.Node, add: Add): void {
  if (!ts.isIdentifier(node)) return;
  if (!forbiddenIdentifiers.has(node.text)) return;
  if (isDeclarationName(node)) return;
  const code = IDENTIFIER_CODES[node.text] ?? 'network-access';
  add(code, node, `Use of "${node.text}" is forbidden in generated code (network / arbitrary code-execution escape hatch).`);
}

function checkMemberAccess(node: ts.Node, add: Add): void {
  if (!ts.isPropertyAccessExpression(node)) return;
  if (!forbiddenMembers.has(node.name.text)) return;
  // Only a network escape when reached through a global object (window.fetch,
  // navigator.sendBeacon); an ordinary object's `.fetch`/`.open` property is not.
  const base = node.expression;
  const isGlobal = ts.isIdentifier(base) && globalObjects.has(base.text);
  const isGlobalNavigator = ts.isPropertyAccessExpression(base) && globalObjects.has(base.name.text);
  if (!isGlobal && !isGlobalNavigator) return;
  add('network-access', node, `Use of "${base.getText()}.${node.name.text}" is forbidden in generated code (network escape hatch).`);
}

function checkNewExpression(node: ts.Node, add: Add): void {
  if (!ts.isNewExpression(node)) return;
  if (!ts.isIdentifier(node.expression)) return;
  const name = node.expression.text;
  if (name === 'Function') add('eval-usage', node, 'Constructing Function() is forbidden in generated code.');
}

function checkJsx(node: ts.Node, add: Add): void {
  if (ts.isJsxOpeningElement(node) || ts.isJsxSelfClosingElement(node)) {
    const tag = node.tagName.getText();
    if (forbiddenElements.has(tag.toLowerCase())) {
      add('forbidden-element', node, `The <${tag}> element is forbidden in generated code.`);
    }
  }
  if (ts.isJsxAttribute(node)) {
    const name = node.name.getText();
    if (forbiddenAttributes.has(name)) {
      add('dangerous-html', node, `The "${name}" attribute is forbidden in generated code.`);
      return;
    }
    // Inline (string-literal) event handlers are an injection vector: `onClick="doThing()"`.
    // React handlers bound to expressions (`onClick={fn}`) are allowed for the widened surface.
    if (/^on[A-Z]/.test(name) && node.initializer && ts.isStringLiteral(node.initializer)) {
      add('inline-event-handler', node, `Inline string event handler "${name}" is forbidden in generated code.`);
    }
  }
}
