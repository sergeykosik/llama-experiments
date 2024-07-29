import path from "path";
import {
  LlamaModel,
  LlamaGrammar,
  LlamaContext,
  LlamaChatSession,
} from "node-llama-cpp";

const grammar = await LlamaGrammar.getFor("json");

export const initializeLlama = async () => {
  const model = new LlamaModel({
    modelPath: path.join(".", "capybarahermes-2.5-mistral-7b.Q5_K_M.gguf"),
    gpuLayers: 64
  });

  

  const context = new LlamaContext({ model });
  const session = new LlamaChatSession({ 
    context, 
    printLLamaSystemInfo: false,
    systemPrompt:  `Transform the following concatenated string lines into interpolated strings. Return the result as a JSON object where each key is the line number and the value is the transformed string. 

    Example:
    Input:
    61: messages.Add("Class " + cl.Name + " with id " + o.Id);
    75: line.Type = "Bank" + (isTransfer ? " (T)" : "");
  
    Output:
    {
        '61': 'messages.Add($"Class {cl.Name} with id {o.Id}");',
        '75': 'line.Type = $"Bank {isTransfer ? " (T)" : ""}";'
    }
    `
   });

   return session;
};

export const processMessage = async (session, message) => {
  if (!session) {
    throw new Error("Llama session is not initialized");
  }

  const response = await session.prompt(message, {
    grammar,
    maxTokens: session.context.getContextSize(),
  });

  return JSON.parse(response);
};

export const shutdownLlama = async (session) => {
    console.log("Shutting down Llama model", session.context);
    await session.context.model.close();
  };
