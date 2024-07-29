import path from "path";
import {
    LlamaModel, LlamaContext
} from "node-llama-cpp";


const model = new LlamaModel({
    modelPath: path.join('.', "capybarahermes-2.5-mistral-7b.Q2_K.gguf")
});

const context = new LlamaContext({model});

const q1 = "Hi there, how are you?";
console.log("AI: " + q1);

const tokens = context.encode(q1);
const res = [];
for await (const modelToken of context.evaluate(tokens)) {
    res.push(modelToken);
    
    // It's important to not concatinate the results as strings,
    // as doing so will break some characters (like some emojis)
    // that consist of multiple tokens.
    // By using an array of tokens, we can decode them correctly together.
    const resString = context.decode(res);
    
    const lastPart = resString.split("ASSISTANT:").reverse()[0];
    if (lastPart.includes("USER:"))
        break;
}

const a1 = context.decode(res).split("USER:")[0];
console.log("AI: " + a1);
