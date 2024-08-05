import path from "path";
import {
  LlamaModel,
  LlamaGrammar,
  LlamaContext,
  LlamaChatSession,
} from "node-llama-cpp";

let session;

const grammar = await LlamaGrammar.getFor("json");
const modelName = "capybarahermes-2.5-mistral-7b.Q5_K_M.gguf";

export const initializeLlama = async () => {
  const model = new LlamaModel({
    modelPath: path.join(".", "models", modelName),
    gpuLayers: 64,
    temperature: 0.5,
    threads: 16,
    contextSize: 8192
  });

  const context = new LlamaContext({ model });
  session = new LlamaChatSession({ 
    context, 
    printLLamaSystemInfo: false,
    conversationHistory: false,
    
    systemPrompt:  `Transform the following concatenated string lines into interpolated strings. Return the result as a JSON object where each key is the line number and the value is the transformed string. 

    Example:
    Input:
    61: messages.Add(cl.Name + " Class " + cl.Type + " with id");
    69: errors.Add("Some text. Id=" + ba.ToIdName().Name);
    75: line.Type = "Bank" + (isTransfer ? " (T)" : "");
    92: companyPractice = new Practice { CompanyName = companyDetails.CompanyName + " practice", CreatedDT = DateTime.Now };
    101: errors.Add((cheque.IsCheque ? Resources.Cheque : "Deposit") + " amount " + cheque.ChequeAmount + " in line " + line.Details + " does not match line amount " + line.Amount);
  
    Output:
    {
        '61': 'messages.Add($"{cl.Name} Class {cl.Type} with id");',
        '69': 'errors.Add($"Some text. Id={ba.ToIdName().Name}");',
        '75': 'line.Type = $"Bank {isTransfer ? " (T)" : ""}";',
        '92': 'companyPractice = new Practice { CompanyName = $"{companyDetails.CompanyName} practice", CreatedDT = DateTime.Now };',
        '101': 'errors.Add($"{cheque.IsCheque ? Resources.Cheque : "Deposit"} amount {cheque.ChequeAmount} in line {line.Details} does not match line amount {line.Amount}");'
    }
    `
   });

   session.temperature = 0.5;
   session.threads = 16;
   session.contextSize = 8192;
};

export const processMessage = async (message) => {
  if (!session) {
    throw new Error("Llama session is not initialized");
  }

  const response = await session.prompt(message, {
    grammar,
    // maxTokens: session.context.getContextSize(),
  });

  return JSON.parse(response);
};
