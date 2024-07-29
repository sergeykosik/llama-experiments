import fs from "fs";
import path from "path";
import axios from "axios";

// Function to read files in a directory and send their content to the API
export const processFilesInDir = async (dir, chunkSize) => {
    try {
      const files = fs.readdirSync(dir);
  
      for (const file of files) {
        const filePath = path.join(dir, file);
        const content = fs.readFileSync(filePath, 'utf-8');
        const chunks = splitIntoChunks(content, chunkSize);
        let transformedContent = '';
  
        for (const chunk of chunks) {
          const prompt = createPrompt(chunk);
          const transformedChunk = await postFileContentToApi(prompt);
          transformedContent += transformedChunk;
        }
  
        // Save the transformed content back to the file
        fs.writeFileSync(filePath, transformedContent, 'utf-8');
        console.log(`File ${file} has been updated.`);
      }
    } catch (error) {
      console.error('Error processing files:', error);
    }
  };

// Function to split text into chunks
const splitIntoChunks = (text, chunkSize) => {
  const chunks = [];
  let start = 0;

  while (start < text.length) {
    let end = start + chunkSize;
    if (end < text.length) {
      end = findLastWhitespace(text, start, end);
    }
    chunks.push(text.slice(start, end));
    start = end;
  }

  return chunks;
};

// Function to find the last whitespace within a given range
const findLastWhitespace = (text, start, end) => {
  for (let i = end; i > start; i--) {
    if (text[i] === " " || text[i] === "\n") {
      return i;
    }
  }
  return end;
};

// Function to create the prompt for the LLM
const createPrompt = (codeSnippet) => {
  return `Transform the following C# code snippet by replacing concatenated strings with interpolated strings and provide the result in JSON format. It should return the fully transformed code snippet with the concatenated strings replaced by interpolated strings. If no transformation is needed, the result should be the same as the input code snippet.
  
  Example:
  Input:
  string greeting = \\"Hello, \\" + name + \\"!\\";
  
  Output:
  {
    "result": "string greeting = $\\"Hello, {name}!\\";"
  }

  Task:
  Input:
  ${codeSnippet}
  `;
};

// Function to post the file content with the prompt to the API
const postFileContentToApi = async (prompt) => {
  try {
    const response = await axios.post(
      "http://localhost:3000/process-message",
      {
        message: prompt,
      },
      {
        headers: {
          "Content-Type": "application/json",
        },
      }
    );
    const transformedData = response.data;
    console.log("Response from API:", transformedData);
    return transformedData.result;
  } catch (error) {
    console.error(
      "Error calling API:",
      error.response ? error.response.data : error.message
    );
  }
};
