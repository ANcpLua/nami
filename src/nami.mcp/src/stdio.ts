import { Transform, type Readable, type TransformCallback, type Writable } from "node:stream";
import { STDIO_DEFAULT_MAX_BUFFER_SIZE, type JSONRPCMessage } from "@modelcontextprotocol/server";
import { StdioServerTransport } from "@modelcontextprotocol/server/stdio";

// JSON-RPC 2.0, section 5.1: input that is not JSON is answered with a Parse error, and because no id could be
// read, the id of that answer is null. The message says nothing about the line: it may hold a credential.
const parseError = { jsonrpc: "2.0", error: { code: -32700, message: "Parse error: the line is not valid JSON." }, id: null };
const blank = /^[ \t\r]*$/;

// The lines of MCP standard input, before the SDK's transport reads them. That transport skips a line that is
// not JSON without a word (its ReadBuffer swallows the SyntaxError), so this stage takes such a line out and
// tells its caller, and passes every other line on unchanged. A line of white space is no message and no error.
class JsonLines extends Transform {
  private pending: Buffer | undefined;
  private readonly refuse: (bytes: number) => void;
  constructor(refuse: (bytes: number) => void) {
    super();
    this.refuse = refuse;
  }
  override _transform(chunk: Buffer, _encoding: BufferEncoding, done: TransformCallback) {
    let rest = this.pending ? Buffer.concat([this.pending, chunk]) : chunk;
    const kept: Buffer[] = [];
    for (let end = rest.indexOf(10); end >= 0; end = rest.indexOf(10)) {
      const text = rest.toString("utf8", 0, end);
      const line = rest.subarray(0, end + 1);
      rest = rest.subarray(end + 1);
      if (blank.test(text)) continue;
      try {
        JSON.parse(text);
        kept.push(line);
      } catch { this.refuse(end); }
    }
    // A line without an end is not held beyond the transport's own bound: past it the bytes go on as they are,
    // and the transport refuses them and closes, as it does without this stage.
    if (rest.length > STDIO_DEFAULT_MAX_BUFFER_SIZE) {
      kept.push(rest);
      rest = Buffer.alloc(0);
    }
    this.pending = rest.length > 0 ? rest : undefined;
    if (kept.length > 0) this.push(kept.length === 1 ? kept[0] : Buffer.concat(kept));
    done();
  }
}

/// The SDK's stdio transport, with a JSON-RPC Parse error (-32700, id null) as the answer to every line that is
/// not JSON, and one line for it through `report`: its length, never its content.
export function stdioTransport(report: (error: Error) => void, stdin: Readable = process.stdin,
  stdout: Writable = process.stdout): StdioServerTransport {
  const lines = new JsonLines((bytes) => {
    report(new Error(`MCP standard input: a line of ${bytes} bytes is not JSON; answered with JSON-RPC error -32700 (Parse error).`));
    transport.send(parseError as unknown as JSONRPCMessage).catch(report);
  });
  stdin.on("error", (error) => lines.destroy(error));
  const transport = new StdioServerTransport(stdin.pipe(lines), stdout);
  return transport;
}
