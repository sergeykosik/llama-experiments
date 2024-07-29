import express from "express";
import bodyParser from "body-parser";
import { initializeLlama, processMessage, shutdownLlama  } from "./llama.service2.js";

const app = express();
const port = 3000;

// Middleware to parse JSON bodies
app.use(bodyParser.json());

// Endpoint to handle POST requests
app.post("/process-message", async (req, res) => {
  const message = req.body.message;

  if (!message) {
    return res.status(400).json({ error: "Message is required" });
  }

  let session;
  try {
    // Initialize Llama model and session
    session = await initializeLlama();
    const startTime = performance.now();
    
    const response = await processMessage(session, message);
    const endTime = performance.now();
    console.log(`LLM response time: ${((endTime - startTime) / 1000).toFixed(2)} seconds`);

    res.json(response);
  } catch (error) {
    console.error("Error processing message with Llama:", error);
    res.status(500).json({ error: "Failed to process message" });
  } finally {
    /* if (session) {
      await shutdownLlama(session);
    } */
  }
});

// Start the server
app.listen(port, () => {
  console.log(`Server is running on http://localhost:${port}`);
});
