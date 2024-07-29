import path from "path";
import {
  LlamaModel,
  LlamaJsonSchemaGrammar,
  LlamaContext,
  LlamaChatSession,
} from "node-llama-cpp";

const model = new LlamaModel({
  modelPath: path.join(".", "capybarahermes-2.5-mistral-7b.Q5_K_M.gguf"),
});

(async () => {
  const grammar = new LlamaJsonSchemaGrammar({
    type: "object",
    properties: {
      responseMessage: {
        type: "string",
      },
      requestPositivityScoreFromOneToTen: {
        type: "number",
      },
    },
  });
  const context = new LlamaContext({ model });
  const session = new LlamaChatSession({ context });

  const q1 = "How are you doing?";
  console.log("User: " + q1);

  const a1 = await session.prompt(q1, {
    grammar,
    maxTokens: context.getContextSize(),
  });

  const parsedA1 = grammar.parse(a1);
  console.log(
    parsedA1
  );

  console.log('size:', context.getContextSize())
})();
